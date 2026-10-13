# WinUtil Upstream Execution Behavior & Architecture Analysis

This document provides a detailed, plain-English analysis of Chris Titus Tech's WinUtil (`github.com/ChrisTitusTech/winutil`) core execution functions based on commit `07ccd8e2e755a706f31569808b31f5b77acad6a9`.

---

## 1. `Invoke-WinUtilTweaks` (`functions/private/Invoke-WinUtilTweaks.ps1`)
- **Purpose**: Applies or reverts all system configurations associated with a selected tweak ID from `config/tweaks.json`.
- **Parameters**:
  - `$CheckBox`: Tweak key name (e.g. `WPFTweaksDisableTelemetry`).
  - `$undo`: Boolean flag. If `$false`, applies tweaks; if `$true`, reverts them.
  - `$KeepServiceStartup`: Boolean flag (default `$true`). Prevents overriding service startup types if the user previously altered them from the Windows default.
- **Execution Flow**:
  1. Sets field mappings based on `$undo`:
     - Apply: `Value` for registry, `StartupType` for service, `InvokeScript` for script, `appx` for package removal.
     - Undo: `OriginalValue` for registry, `OriginalType` for service, `UndoScript` for script.
  2. **Services**: For each service in the tweak definition, checks current startup type against `OriginalType` if `$KeepServiceStartup` is active. If modified by user, leaves it alone. Otherwise calls `Set-WinUtilService`.
  3. **Registry**: For each registry entry, calls `Set-WinUtilRegistry`.
  4. **Scripts**: For each script block in `InvokeScript` or `UndoScript`, creates a script block and executes via `Invoke-WinUtilScript`.
  5. **AppX**: If applying (`!$undo`), removes installed per-user packages via `Remove-WinUtilAPPX` and provisioned system images via `Remove-WinUtilProvisionedAPPX`.
  6. Tracks error counts via runspace-isolated `$global:WinUtilJobErrorCount`.

---

## 2. `Set-WinUtilRegistry` (`functions/private/Set-WinUtilRegistry.ps1`)
- **Purpose**: Low-level registry writer and remover.
- **Parameters**: `$Name`, `$Path`, `$Type`, `$Value`.
- **Special Upstream Semantics**:
  - **Drive Mount**: Automatically checks if `HKU:\` (`HKEY_USERS`) PSDrive is mounted; mounts it if missing.
  - **Directory Creation**: If the target key `$Path` does not exist, creates it using `New-Item -Path $Path -Force`.
  - **`<RemoveEntry>` Magic Value**: If `$Value -eq "<RemoveEntry>"`, calls `Remove-ItemProperty -Path $Path -Name $Name -Force` to completely delete the property rather than writing a null or empty value.
  - **Value Writing**: Otherwise calls `Set-ItemProperty -Path $Path -Name $Name -Type $Type -Value $Value -Force`.
  - **Error Handling**: Distinct catch blocks for `SecurityException`, `ItemNotFoundException`, `UnauthorizedAccessException`, and generic exceptions, logging warnings without crashing the execution queue.

---

## 3. `Set-WinUtilService` (`functions/private/Set-WinUtilService.ps1`)
- **Purpose**: Safely configures Windows service startup configurations.
- **Parameters**: `$Name`, `$StartupType` (`Automatic`, `AutomaticDelayedStart`, `Manual`, `Disabled`).
- **Execution Details**:
  - Checks if the service exists using `Get-Service -Name $Name`.
  - If current `StartType` already matches target `$StartupType`, logs no-op and exits cleanly.
  - Handles `AutomaticDelayedStart` compatibility for PowerShell 5.1 via `sc.exe config $Name start= delayed-auto`.
  - Uses `Set-Service -StartupType $StartupType` for standard types.
  - Catches `NoServiceFoundForGivenName` gracefully as a warning (service may not be present on specific Windows editions, e.g. Home vs Pro).

---

## 4. `Set-WinUtilScheduledTask` & Task Actions (`functions/` & `config/feature.json`)
- **Purpose**: Enables, disables, or registers Windows Task Scheduler jobs.
- **Execution Details**:
  - Uses `Get-ScheduledTask -TaskPath ... | Disable-ScheduledTask` or `Enable-ScheduledTask`.
  - Used prominently in Windows Update configuration (`\Microsoft\Windows\InstallService\*`, `\Microsoft\Windows\UpdateOrchestrator\*`, etc.) and system maintenance scripts (e.g. `AutoRegBackup`).
  - WinUtil handles missing task paths with `-ErrorAction SilentlyContinue` so non-existent tasks do not abort batch runs.

---

## 5. `Invoke-WinUtilScript` (`functions/private/Invoke-WinUtilScript.ps1`)
- **Purpose**: Executes custom inline PowerShell script blocks that cannot be expressed purely via declarative registry or service items.
- **Parameters**: `$Name`, `$scriptblock`.
- **Execution Details**:
  - Runs `$scriptblock` using `Invoke-Command`.
  - Granular exception capture: `CommandNotFoundException`, `RuntimeException`, `SecurityException`, `UnauthorizedAccessException`.
  - Logs execution details to the WinUtil job log and component logger.

---

## 6. `Remove-WinUtilAPPX` & `Remove-WinUtilProvisionedAPPX`
- **Purpose**: Strips bloatware modern apps for both currently logged-in users and future provisioned accounts.
- **`Remove-WinUtilAPPX.ps1`**:
  - Uses `Get-AppxPackage "*$Name*" -AllUsers | Sort-Object -Property PackageFullName -Unique`.
  - Iterates over each package and calls `Remove-AppxPackage -Package $pkg.PackageFullName -AllUsers -ErrorAction Stop`.
  - Explicit foreach iteration prevents silent pipeline failures present in PowerShell 7.
- **`Remove-WinUtilProvisionedAPPX.ps1`**:
  - Dismounts provisioned app packages using DISM.
  - Because `Get-AppxProvisionedPackage` often causes COM errors or hangs in PowerShell 7, WinUtil shells out to Windows PowerShell 5.1 (`powershell.exe -NoProfile -NonInteractive -Command ...`) to remove provisioned packages cleanly via `Remove-AppxProvisionedPackage -Online -PackageName $prov.PackageName`.

---

## 7. `Invoke-WinUtilFeatureInstall` (`functions/private/Invoke-WinUtilFeatureInstall.ps1`)
- **Purpose**: Installs or enables optional Windows capabilities and features (e.g., .NET 3.5, Hyper-V, WSL, OpenSSH Server).
- **Execution Details**:
  - For items declared in `feature` list: calls `Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart -ErrorAction Stop`.
  - For items with `InvokeScript`: runs script blocks to download, configure, or initialize additional components.

---

## 8. `Invoke-WPFFixesUpdate` (`functions/public/Invoke-WPFFixesUpdate.ps1`)
- **Purpose**: Comprehensive deep repair of the Windows Update subsystem.
- **Execution Steps**:
  1. Scans system component store using `DISM /Online /Cleanup-Image /RestoreHealth`.
  2. Stops update services: `wuauserv`, `BITS`, `CryptSvc`, `TrustedInstaller`, `AppIDSvc`.
  3. Renames cache folders: `C:\Windows\SoftwareDistribution` -> `*.bak`, `C:\Windows\System32\catroot2` -> `*.bak`.
  4. Resets Windows Update security descriptors (`sdset`).
  5. Re-registers all core update DLLs (`atl.dll`, `urlmon.dll`, `mshtml.dll`, `wups.dll`, `wuaueng.dll`, etc.).
  6. Clears corrupt Group Policy objects from registry and deletes `System32\GroupPolicy` and `GroupPolicyUsers`.
  7. Resets networking stacks: `netsh winsock reset`, `netsh winhttp reset proxy`, `netsh int ip reset`.
  8. Clears queued BITS transfers via `Get-BitsTransfer | Remove-BitsTransfer`.
  9. Restores service startup types to Manual and starts services (`BITS`, `wuauserv`, `CryptSvc`, `AppIDSvc`).
  10. Forces update detection via COM `Microsoft.Update.AutoUpdate.DetectNow()` and `wuauclt /resetauthorization /detectnow`.

---

## 9. `Invoke-WPFFixesNetwork` (`functions/public/Invoke-WPFFixesNetwork.ps1`)
- **Purpose**: Network stack reset.
- **Execution Details**:
  - Executes `netsh winsock reset` to restore the Windows Sockets API catalog.
  - Executes `netsh int ip reset` to rewrite TCP/IP registry keys.
  - Notifies user to reboot for new catalog binding.

---

## 10. `Invoke-WPFSystemRepair` (`functions/public/Invoke-WPFSystemRepair.ps1`)
- **Purpose**: System file and disk health repair.
- **Execution Details**:
  - Step 1: Disk check via `chkdsk /scan /perf` (accepts exit codes 0, 1, 2 as success).
  - Step 2: System file check via `sfc /scannow` (requires exit code 0).
  - Step 3: DISM image repair via `dism /online /cleanup-image /restorehealth` (accepts 0 and 3010 reboot required).

---

## 11. `Invoke-WPFUpdates*` (Default, Security, Disable)
- **`Invoke-WPFUpdatesdefault`**:
  - Clears `NoAutoUpdate`, `AUOptions`, and all deferral policies from `HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`.
  - Sets services `BITS` (Manual), `wuauserv` (Manual), `UsoSvc` (Automatic).
  - Re-enables all scheduled tasks under `\Microsoft\Windows\Update*`.
- **`Invoke-WPFUpdatessecurity` (Recommended)**:
  - Disables driver offering via Windows Update (`ExcludeWUDriversInQualityUpdate = 1`, `DontSearchWindowsUpdate = 1`).
  - Defers Feature Updates by 365 days (`DeferFeatureUpdates = 1`, `DeferFeatureUpdatesPeriodInDays = 365`).
  - Defers Quality Updates by 4 days (`DeferQualityUpdates = 1`, `DeferQualityUpdatesPeriodInDays = 4`).
  - Sets `AUOptions = 3` (automatically download and notify before installation).
- **`Invoke-WPFUpdatesdisable` (High Risk)**:
  - Requires user confirmation.
  - Sets `NoAutoUpdate = 1`, `AUOptions = 1`, `DODownloadMode = 0`.
  - Stops and disables `BITS`, `wuauserv`, `UsoSvc`.
  - Purges `C:\Windows\SoftwareDistribution\*`.
  - Disables all scheduled tasks in `\Microsoft\Windows\InstallService\*`, `\Microsoft\Windows\UpdateOrchestrator\*`, `\Microsoft\Windows\UpdateAssistant\*`, `\Microsoft\Windows\WaaSMedic\*`, `\Microsoft\Windows\WindowsUpdate\*`.

---

## 12. `Invoke-WPFtweaksbutton` (`functions/public/Invoke-WPFtweaksbutton.ps1`)
- **Purpose**: Batch orchestrator for applying user-selected tweaks.
- **Execution Details**:
  - Validates that at least one tweak or DNS setting is selected.
  - If `WPFTweaksRestorePoint` is selected, creates the System Restore Point first before any modifications.
  - If DNS is selected, runs `Set-WinUtilDNS`. If DNS fails, aborts remainder to prevent partial network isolation.
  - Iterates through all remaining tweaks, invoking `Invoke-WinUtilTweaks $tweak` sequentially with job progress reporting.

---

## 13. `Invoke-WPFundoall` (`functions/public/Invoke-WPFundoall.ps1`)
- **Purpose**: Batch orchestrator for reverting user-selected tweaks.
- **Execution Details**:
  - Iterates through `$sync.selectedTweaks`.
  - Invokes `Invoke-WinUtilTweaks $tweak -undo $true`.
  - Restores `OriginalValue` for registry and `OriginalType` for services.

---

## 14. `Install-WinUtilProgramWinget` (`functions/private/Install-WinUtilProgramWinget.ps1`)
- **Purpose**: Robust WinGet package manager installer, upgrader, and uninstaller.
- **Execution Details**:
  - Executes package actions individually so failures can be isolated to the exact app ID.
  - Supports `msstore:` prefix to redirect source to `msstore`.
  - Handles exit codes accurately:
    - `0`: Succeeded.
    - `3010`, `1641`, `-1978334967`, `-1978334965`: Succeeded (restart required/initiated).
    - `-1978335135`: Skipped (already installed).
    - `-1978335189`: Skipped (no applicable update).
    - `-1978335107` (`APPINSTALLER_CLI_ERROR_ADMIN_CONTEXT_ACTION_PROHIBITED`): Skipped with informative message that elevated process cannot modify per-user package.
    - Other codes: Reported with hexadecimal HRESULT formatted error code.

---

## 15. `Get-WinUtilToggleStatus` (`functions/private/Get-WinUtilToggleStatus.ps1`)
- **Purpose**: Inspects active Windows registry state to determine if a toggle switch should be displayed as ON or OFF.
- **Execution Details**:
  - Mounts `HKU:\` PSDrive if needed.
  - Iterates through all registry entries associated with the tweak.
  - If registry key is missing, falls back to `DefaultState` (`"true"` maps to `Value`, `"false"` maps to `OriginalValue`).
  - If any entry does not match `Value`, returns `false`.
  - If all entries match `Value`, returns `true`.

---

## 16. `Set-WinUtilDNS` (`functions/private/Set-WinUtilDNS.ps1`)
- **Purpose**: Configures network adapter DNS servers with optional DNS-over-HTTPS (DoH).
- **Execution Details**:
  - Identifies active network adapters with `Get-NetAdapter | Where-Object { $_.Status -eq 'Up' }`.
  - If set to `DHCP`: resets adapters using `Set-DnsClientServerAddress -ResetServerAddresses` and `netsh interface ip/ipv6 set dnsservers source=dhcp`, and purges DoH interface settings.
  - If set to a named provider: fetches primary and secondary IPv4 and IPv6 addresses from `config/dns.json`.
  - If system supports DoH (`Add-DnsClientDohServerAddress`), registers the provider's HTTPS template and enables `DohFlags = 1` in the Dnscache registry interface.
  - Flushes DNS cache using `Clear-DnsClientCache`.
