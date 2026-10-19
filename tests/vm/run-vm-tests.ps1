# ==============================================================================
# WinVelo Host-Side VirtualBox VM Test Orchestrator
# Runs on the host machine to control the VirtualBox test VM and execute real-mode tests
# ==============================================================================

param(
    [string]$VmName = "WinVelo-Test-VM",
    [string]$SnapshotName = "clean",
    [string]$GuestUser = "winvelo-admin",
    [string]$GuestPassword = "Password123!",
    [string]$VBoxManagePath = "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe",
    [switch]$Headless
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   WinVelo Host VirtualBox Test Orchestrator (Phase C)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "VM Name:       $VmName"
Write-Host "Snapshot:      $SnapshotName"
Write-Host "VBoxManage:    $VBoxManagePath"
Write-Host ""

# 1. Verify VBoxManage exists
if (-not (Test-Path $VBoxManagePath)) {
    Write-Host "[ERROR] VBoxManage.exe not found at '$VBoxManagePath'." -ForegroundColor Red
    Write-Host "Please install VirtualBox or verify its installation path." -ForegroundColor Yellow
    exit 1
}

# 2. Verify VM exists
$vmList = & "$VBoxManagePath" list vms
if ($vmList -notmatch [regex]::Escape($VmName)) {
    Write-Host "[ERROR] Virtual machine '$VmName' was not found in VirtualBox." -ForegroundColor Red
    Write-Host "Please follow docs/VM_SETUP.md to create the VM before running this script." -ForegroundColor Yellow
    exit 1
}

# 3. Publish latest CLI and Catalog for the VM
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$guestStagingDir = Join-Path $repoRoot "tests\vm\staging"
if (Test-Path $guestStagingDir) { Remove-Item $guestStagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $guestStagingDir -Force | Out-Null

Write-Host "[1/7] Building and staging WinVelo CLI for guest..." -ForegroundColor Cyan
$publishCmd = "dotnet publish `"$repoRoot\src\WinVelo.Cli\WinVelo.Cli.csproj`" -c Release -o `"$guestStagingDir`""
Invoke-Expression $publishCmd

# Copy Catalog & test runner into staging
Copy-Item (Join-Path $repoRoot "Catalog") -Destination $guestStagingDir -Recurse -Force
Copy-Item (Join-Path $repoRoot "tests\vm\run-integration.ps1") -Destination $guestStagingDir -Force

Write-Host "[INFO] Staged $(Get-ChildItem $guestStagingDir | Measure-Object | Select-Object -ExpandProperty Count) items in $guestStagingDir" -ForegroundColor DarkGray

# 4. Restore "clean" snapshot
Write-Host "`n[2/7] Restoring snapshot '$SnapshotName' for VM '$VmName'..." -ForegroundColor Cyan
& "$VBoxManagePath" controlvm "$VmName" poweroff 2>$null
Start-Sleep -Seconds 2
& "$VBoxManagePath" snapshot "$VmName" restore "$SnapshotName"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Failed to restore snapshot '$SnapshotName'." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Snapshot restored successfully." -ForegroundColor Green

# 5. Start VM
$startType = if ($Headless) { "headless" } else { "gui" }
Write-Host "`n[3/7] Starting VM '$VmName' in $startType mode..." -ForegroundColor Cyan
& "$VBoxManagePath" startvm "$VmName" --type $startType
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Failed to start VM." -ForegroundColor Red
    exit 1
}

# 6. Wait for Guest Additions & OS to become ready
Write-Host "`n[4/7] Waiting for Windows 11 guest to boot and become ready..." -ForegroundColor Cyan
$maxWaitSec = 180
$waited = 0
$guestReady = $false

while ($waited -lt $maxWaitSec) {
    Start-Sleep -Seconds 5
    $waited += 5
    Write-Host "." -NoNewline -ForegroundColor DarkGray

    $sessionOutput = & "$VBoxManagePath" guestcontrol "$VmName" --username "$GuestUser" --password "$GuestPassword" run --exe "C:\Windows\System32\cmd.exe" -- /c echo READY 2>&1
    if ($sessionOutput -match "READY") {
        $guestReady = $true
        break
    }
}
Write-Host ""

if (-not $guestReady) {
    Write-Host "[ERROR] Guest OS did not become responsive after $maxWaitSec seconds." -ForegroundColor Red
    & "$VBoxManagePath" controlvm "$VmName" poweroff
    exit 1
}
Write-Host "[PASS] Guest Windows 11 OS is responsive." -ForegroundColor Green

# 7. Copy staging directory into guest C:\WinVeloTest
Write-Host "`n[5/7] Deploying WinVelo package into guest at C:\WinVeloTest..." -ForegroundColor Cyan
& "$VBoxManagePath" guestcontrol "$VmName" --username "$GuestUser" --password "$GuestPassword" createdir "C:\WinVeloTest" 2>&1 | Out-Null
& "$VBoxManagePath" guestcontrol "$VmName" --username "$GuestUser" --password "$GuestPassword" copyto --target-directory "C:\WinVeloTest" --recursive "$guestStagingDir\*" 2>&1 | Out-Null
Write-Host "[PASS] Package deployed to guest." -ForegroundColor Green

# 8. Run integration test script inside guest
Write-Host "`n[6/7] Running tests/vm/run-integration.ps1 inside guest as administrator..." -ForegroundColor Cyan
$testOut = & "$VBoxManagePath" guestcontrol "$VmName" --username "$GuestUser" --password "$GuestPassword" run --exe "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" -- -NoProfile -ExecutionPolicy Bypass -File "C:\WinVeloTest\run-integration.ps1" 2>&1
Write-Host $testOut

# 9. Copy back integration-results.json to host
$resultsDir = Join-Path $repoRoot "tests\vm\results"
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null }
$hostResultJson = Join-Path $resultsDir "integration-results.json"

& "$VBoxManagePath" guestcontrol "$VmName" --username "$GuestUser" --password "$GuestPassword" copyfrom "C:\WinVeloTest\integration-results.json" --target-directory "$resultsDir" 2>&1 | Out-Null

# 10. Power off and restore clean snapshot
Write-Host "`n[7/7] Powering off and reverting VM to clean snapshot..." -ForegroundColor Cyan
& "$VBoxManagePath" controlvm "$VmName" poweroff
Start-Sleep -Seconds 2
& "$VBoxManagePath" snapshot "$VmName" restore "$SnapshotName"
Write-Host "[PASS] VM cleaned and restored." -ForegroundColor Green

# 11. Final Report on Host
if (Test-Path $hostResultJson) {
    $report = Get-Content $hostResultJson -Raw | ConvertFrom-Json
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host "               GUEST TEST EXECUTION REPORT                " -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "Suite:        $($report.TestSuite)"
    Write-Host "Guest OS:     $($report.HostOS) (Build $($report.OSVersion))"
    Write-Host "Pass Count:   $($report.PassCount)" -ForegroundColor Green
    Write-Host "Fail Count:   $($report.FailCount)" -ForegroundColor $(if ($report.FailCount -eq 0) { "Green" } else { "Red" })
    Write-Host "Duration:     $($report.DurationSec)s"
    Write-Host "Results File: $hostResultJson"

    if ($report.FailCount -gt 0) {
        exit 1
    } else {
        exit 0
    }
} else {
    Write-Host "[ERROR] Could not retrieve integration-results.json from guest." -ForegroundColor Red
    exit 1
}
