# Tweakora Host PC Manual Verification Test Plan

This document is a step-by-step checklist for testing Tweakora directly on your real Windows PC.
It is organized into 3 safety levels.

---

## 🛡️ General Safety Rules Before Starting

1. **Run as Administrator**: Right-click `Tweakora.exe` and select **Run as administrator**.
2. **Create a System Restore Point**:
   ```powershell
   Checkpoint-Computer -Description "Before Tweakora Testing" -RestorePointType "MODIFY_SETTINGS"
   ```
3. **Preview All Changes**: Always check the Diff Preview dialog before clicking Apply.
4. **All Changes are Logged**: Every change is tracked in `%APPDATA%\Tweakora\Journal`.

---

## 🟢 LEVEL 1: Safe Verification (HKCU & User Tools Only)
*Zero system danger. Only modifies user-level preferences and safe caches.*

### 1. Smart Cleaner (User Temporary Files)
* **What to click**:
  1. Open the **Toolbox** tab.
  2. Under **Smart Cleaner**, click **Scan System**.
  3. Ensure only **User Temporary Files** (`%LOCALAPPDATA%\Temp`) is selected.
  4. Click **Clean Selected**.
* **What you should see**:
  - The scan finds temporary files and displays the total size.
  - Clicking clean deletes the files and updates status: *"Cleanup completed! Deleted N files..."*.
* **How to check it really worked**:
  Create a test canary file before cleaning:
  ```powershell
  New-Item "$env:LOCALAPPDATA\Temp\tweakora_canary.tmp" -ItemType File -Value "CanaryTest"
  ```
  After clicking **Clean Selected**, verify the file is gone:
  ```powershell
  Test-Path "$env:LOCALAPPDATA\Temp\tweakora_canary.tmp"
  # Expected output: False
  ```
* **How to undo**: Temporary cache files do not need to be restored.

---

### 2. Legacy Control Panel Shortcuts
* **What to click**:
  1. Open the **Config** tab.
  2. Under **Legacy Control Panels**, click **System Properties** (`sysdm.cpl`).
  3. Click **Programs and Features** (`appwiz.cpl`).
* **What you should see**:
  - The classic Windows System Properties dialog opens immediately.
  - The classic Programs and Features dialog opens immediately.
* **How to check it really worked**:
  - The native Windows applets appear on your screen.
* **How to undo**:
  - Simply close the dialog windows.

---

### 3. File Explorer: Always Show File Extensions (`ui.explorer.show-file-extensions`)
* **What to click**:
  1. Open the **Tweaks** tab.
  2. Type `extensions` in the search box.
  3. Toggle **Always Show Known File Extensions** to ON.
  4. Notice the top button displays **Apply Staged (1)**.
  5. Click **Apply Staged (1)** and inspect the Diff Preview dialog.
  6. Click **Confirm & Apply**.
* **What you should see**:
  - Diff dialog shows `HideFileExt: 1 -> 0`.
  - Status updates to *"Execution completed"*.
  - File Explorer now displays `.txt`, `.docx`, `.exe` extensions on all files.
* **How to check it really worked**:
  ```powershell
  Get-ItemPropertyValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" -Name "HideFileExt"
  # Expected output: 0 (Visible)
  ```
* **How to undo**:
  1. In Tweakora, toggle the switch to OFF.
  2. Click **Apply Staged (1)** (or select and click **Undo Selected**).
* **How to check undo worked**:
  ```powershell
  Get-ItemPropertyValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" -Name "HideFileExt"
  # Expected output: 1 (Hidden)
  ```

---

### 4. Taskbar: End Task on Right-Click (`ui.taskbar.end-task-right-click`)
* **What to click**:
  1. Open the **Tweaks** tab.
  2. Search for `end task`.
  3. Toggle **Enable 'End Task' in Taskbar Context Menu** to ON.
  4. Click **Apply Staged (1)** and confirm.
* **What you should see**:
  - Right-clicking any running app on your Windows taskbar now shows an **End Task** option.
* **How to check it really worked**:
  ```powershell
  Get-ItemPropertyValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings" -Name "TaskbarEndTask"
  # Expected output: 1
  ```
* **How to undo**:
  1. Toggle the switch to OFF and click **Apply Staged (1)**.
* **How to check undo worked**:
  ```powershell
  Get-ItemPropertyValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings" -Name "TaskbarEndTask"
  # Expected output: 0 (or key absent)
  ```

---

### 5. DNS Switcher: Cloudflare 1.1.1.1
* **What to click**:
  1. Open the **Toolbox** tab.
  2. Under **DNS Switcher**, click **Set DNS** next to **Cloudflare DNS** (`1.1.1.1`).
* **What you should see**:
  - Status confirms *"Applied Cloudflare DNS successfully"*.
  - The active badge moves to Cloudflare.
* **How to check it really worked**:
  ```powershell
  (Get-DnsClientServerAddress -AddressFamily IPv4 | Where-Object { $_.ServerAddresses.Count -gt 0 }).ServerAddresses
  # Expected output includes: 1.1.1.1 and 1.0.0.1
  ```
* **How to undo**:
  Run this PowerShell command to revert back to your automatic router DHCP DNS:
  ```powershell
  Get-NetAdapter | Where-Object { $_.Status -eq "Up" } | Set-DnsClientServerAddress -ResetServerAddresses
  ```
* **How to check undo worked**:
  ```powershell
  (Get-DnsClientServerAddress -AddressFamily IPv4 | Where-Object { $_.ServerAddresses.Count -gt 0 }).ServerAddresses
  # Expected output: Your normal local gateway IP (e.g. 192.168.1.1)
  ```

---

## 🟡 LEVEL 2: Low Risk (System Configuration & Features)
*Modifies non-destructive system settings and optional components.*

### 1. Privacy: Disable Advertising ID (`privacy.advertising.advertising-id`)
* **What to click**:
  1. Open **Tweaks** tab > Search `advertising`.
  2. Enable **Disable Advertising ID** > Click **Apply Staged (1)**.
* **How to check it really worked**:
  ```powershell
  Get-ItemPropertyValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo" -Name "Enabled"
  # Expected output: 0
  ```
* **How to undo**:
  - Revert toggle in Tweakora and click Apply.
  ```powershell
  Get-ItemPropertyValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo" -Name "Enabled"
  # Expected output: 1
  ```

### 2. Optional Windows Features: Telnet Client or TFTP
* **What to click**:
  1. Open **Config** tab > Search `Telnet` or `TFTP`.
  2. Select the feature > Click **Enable Features**.
* **What you should see**:
  - DISM progress appears, and status confirms *"Enable features completed"*.
* **How to check it really worked**:
  ```powershell
  Get-WindowsOptionalFeature -Online -FeatureName "TelnetClient"
  # State: Enabled
  ```
* **How to undo**:
  - Select feature in Tweakora > Click **Disable Features**.
* **How to check undo worked**:
  ```powershell
  Get-WindowsOptionalFeature -Online -FeatureName "TelnetClient"
  # State: Disabled
  ```

### 3. Ultimate Performance Power Plan
* **What to click**:
  1. In **Tweaks** tab, change the **Power Plan** dropdown to **Ultimate Performance**.
  2. Click **Apply Staged**.
* **How to check it really worked**:
  ```powershell
  powercfg /getactivescheme
  # Shows: Ultimate Performance
  ```
* **How to undo**:
  - Change the dropdown back to **Balanced (Recommended)**.
  ```powershell
  powercfg /getactivescheme
  # Shows: Balanced
  ```

---

## 🔴 LEVEL 3: High Risk (DO NOT TEST ON HOST PC NOW)
*These tweaks alter system-wide security, drivers, or essential services. Reserve for virtual machines.*

| Tweak ID / Feature | Risk | Rationale / Potential Impact |
|---|:---:|---|
| `advanced.disablebitlocker` | **HIGH** | Starts drive decryption. Takes hours and removes drive encryption. |
| `advanced.removeedge` | **HIGH** | Strips Microsoft Edge files. Can break apps embedding WebView2. |
| `advanced.removeonedrive` | **HIGH** | Uninstalls OneDrive. Can cause missing folder references in Explorer. |
| `advanced.windowsai` / Recall | **HIGH** | Disables AI dependencies that may be integrated with system search. |
| `advanced.disableipv6` | **HIGH** | Disables IPv6 protocol. Breaks home networking and Xbox party services. |
| `UpdateProfileMode.DisableAll` | **HIGH** | Halts Windows Update service completely. Leaves PC unpatched. |
| `essential.services` (Manual) | **HIGH** | Switches diagnostic & delivery services to Manual. Can affect background sync. |

---

## ⚡ Automated CLI Self-Test (Level 1 Safe HKCU)

You can also run the automated Level 1 verification directly from an Administrator PowerShell prompt:

```powershell
.\tweakora-cli.exe --self-test --level 1
```

This command will:
1. Create a System Restore Point (`Tweakora Pre-Self-Test Level 1`).
2. Test every safe Level 1 tweak (Read -> Apply -> Verify -> Undo -> Verify exact original restored).
3. Display a PASS/FAIL table in the console.
4. Save the full audit report to `self-test-results.md`.
