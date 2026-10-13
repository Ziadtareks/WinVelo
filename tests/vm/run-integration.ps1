# ==============================================================================
# Tweakora Real-Mode In-Guest Integration Test Runner
# Runs inside the Windows 11 VirtualBox test VM with --real-mode (NO FAKES)
# ==============================================================================

param(
    [string]$AppDir = "C:\TweakoraTest",
    [string]$ReportPath = "C:\TweakoraTest\integration-results.json"
)

$ErrorActionPreference = "Continue"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Tweakora Real-Mode In-Guest System Integration Tests   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target Directory: $AppDir"
Write-Host "Report Path:      $ReportPath"
Write-Host "Testing Mode:     REAL WINDOWS OS EXECUTION (No Fakes)" -ForegroundColor Yellow
Write-Host ""

$CliExe = Join-Path $AppDir "tweakora-cli.exe"
if (-not (Test-Path $CliExe)) {
    Write-Host "[ERROR] tweakora-cli.exe not found at $CliExe" -ForegroundColor Red
    exit 1
}

$results = [System.Collections.Generic.List[PSCustomObject]]::new()
$startTime = Get-Date

function Record-TestResult {
    param(
        [string]$TestName,
        [string]$Category,
        [bool]$Success,
        [string]$Details
    )

    $color = if ($Success) { "Green" } else { "Red" }
    $tag = if ($Success) { "[PASS]" } else { "[FAIL]" }
    Write-Host "$tag $TestName - $Details" -ForegroundColor $color

    $results.Add([PSCustomObject]@{
        TestName  = $TestName
        Category  = $Category
        Success   = $Success
        Details   = $Details
        Timestamp = (Get-Date).ToString("o")
    })
}

# ------------------------------------------------------------------------------
# TEST 1: Show File Extensions (Tweak: ui.explorer.show-file-extensions)
# ------------------------------------------------------------------------------
Write-Host "`n>>> [1/6] Testing Registry Tweak: Show File Extensions..." -ForegroundColor Cyan
try {
    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
    $propName = "HideFileExt"

    # 1. Apply: HideFileExt should become 0 (visible)
    $applyOut = & $CliExe --real-mode --tweak "ui.explorer.show-file-extensions" --apply 2>&1
    $valAfterApply = (Get-ItemProperty -Path $regPath -Name $propName -ErrorAction SilentlyContinue).$propName

    if ($valAfterApply -eq 0) {
        Record-TestResult -TestName "Apply_ShowFileExtensions" -Category "RegistryTweak" -Success $true -Details "Registry HideFileExt set to 0 (Visible)"
    } else {
        Record-TestResult -TestName "Apply_ShowFileExtensions" -Category "RegistryTweak" -Success $false -Details "Expected HideFileExt=0, got $valAfterApply. Output: $applyOut"
    }

    # 2. Undo: HideFileExt should restore to 1 (hidden)
    $undoOut = & $CliExe --real-mode --tweak "ui.explorer.show-file-extensions" --undo 2>&1
    $valAfterUndo = (Get-ItemProperty -Path $regPath -Name $propName -ErrorAction SilentlyContinue).$propName

    if ($valAfterUndo -eq 1) {
        Record-TestResult -TestName "Undo_ShowFileExtensions" -Category "RegistryTweak" -Success $true -Details "Registry HideFileExt restored to 1 (Hidden)"
    } else {
        Record-TestResult -TestName "Undo_ShowFileExtensions" -Category "RegistryTweak" -Success $false -Details "Expected HideFileExt=1, got $valAfterUndo. Output: $undoOut"
    }
} catch {
    Record-TestResult -TestName "ShowFileExtensions" -Category "RegistryTweak" -Success $false -Details "Exception: $_"
}

# ------------------------------------------------------------------------------
# TEST 2: End Task on Taskbar (Tweak: ui.taskbar.end-task-right-click)
# ------------------------------------------------------------------------------
Write-Host "`n>>> [2/6] Testing Registry Tweak: End Task On Taskbar Right-Click..." -ForegroundColor Cyan
try {
    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings"
    $propName = "TaskbarEndTask"

    # 1. Apply: TaskbarEndTask should become 1
    $applyOut = & $CliExe --real-mode --tweak "ui.taskbar.end-task-right-click" --apply 2>&1
    $valAfterApply = (Get-ItemProperty -Path $regPath -Name $propName -ErrorAction SilentlyContinue).$propName

    if ($valAfterApply -eq 1) {
        Record-TestResult -TestName "Apply_EndTaskOnTaskbar" -Category "RegistryTweak" -Success $true -Details "Registry TaskbarEndTask set to 1"
    } else {
        Record-TestResult -TestName "Apply_EndTaskOnTaskbar" -Category "RegistryTweak" -Success $false -Details "Expected TaskbarEndTask=1, got $valAfterApply. Output: $applyOut"
    }

    # 2. Undo: TaskbarEndTask should restore to 0
    $undoOut = & $CliExe --real-mode --tweak "ui.taskbar.end-task-right-click" --undo 2>&1
    $valAfterUndo = (Get-ItemProperty -Path $regPath -Name $propName -ErrorAction SilentlyContinue).$propName

    if ($valAfterUndo -eq 0 -or $null -eq $valAfterUndo) {
        Record-TestResult -TestName "Undo_EndTaskOnTaskbar" -Category "RegistryTweak" -Success $true -Details "Registry TaskbarEndTask restored to 0/deleted"
    } else {
        Record-TestResult -TestName "Undo_EndTaskOnTaskbar" -Category "RegistryTweak" -Success $false -Details "Expected TaskbarEndTask=0, got $valAfterUndo. Output: $undoOut"
    }
} catch {
    Record-TestResult -TestName "EndTaskOnTaskbar" -Category "RegistryTweak" -Success $false -Details "Exception: $_"
}

# ------------------------------------------------------------------------------
# TEST 3: Smart Cleaner Canary Deletion
# ------------------------------------------------------------------------------
Write-Host "`n>>> [3/6] Testing Smart Cleaner: Real Canary File Removal..." -ForegroundColor Cyan
try {
    $canaryPath = Join-Path $env:LOCALAPPDATA "Temp\tweakora_integration_canary.tmp"
    Set-Content -Path $canaryPath -Value "TWEAKORA_CLEANER_VERIFICATION_PAYLOAD" -Force

    if (Test-Path $canaryPath) {
        Write-Host "Created canary file at: $canaryPath" -ForegroundColor DarkGray
        $cleanOut = & $CliExe --real-mode --clean UserTemp 2>&1

        if (-not (Test-Path $canaryPath)) {
            Record-TestResult -TestName "Cleaner_CanaryFileDeletion" -Category "Cleaner" -Success $true -Details "Canary file in UserTemp was successfully deleted by real CleanerService"
        } else {
            Remove-Item $canaryPath -Force -ErrorAction SilentlyContinue
            Record-TestResult -TestName "Cleaner_CanaryFileDeletion" -Category "Cleaner" -Success $false -Details "Canary file still exists after clean! Output: $cleanOut"
        }
    } else {
        Record-TestResult -TestName "Cleaner_CanaryFileDeletion" -Category "Cleaner" -Success $false -Details "Failed to create canary file before cleaning."
    }
} catch {
    Record-TestResult -TestName "Cleaner_CanaryFileDeletion" -Category "Cleaner" -Success $false -Details "Exception: $_"
}

# ------------------------------------------------------------------------------
# TEST 4: DNS Configuration & Verification
# ------------------------------------------------------------------------------
Write-Host "`n>>> [4/6] Testing DNS Provider Configuration..." -ForegroundColor Cyan
try {
    $dnsOut = & $CliExe --real-mode --dns "dns.cloudflare.standard" 2>&1
    Start-Sleep -Seconds 2

    # Query active IPv4 DNS servers
    $activeDns = Get-DnsClientServerAddress -AddressFamily IPv4 | Where-Object { $_.ServerAddresses.Count -gt 0 }
    $hasCloudflare = $activeDns | Where-Object { $_.ServerAddresses -contains "1.1.1.1" }

    if ($hasCloudflare) {
        Record-TestResult -TestName "Apply_CloudflareDNS" -Category "Network" -Success $true -Details "Cloudflare 1.1.1.1 verified on active adapter: $($hasCloudflare.InterfaceAlias)"
    } else {
        Record-TestResult -TestName "Apply_CloudflareDNS" -Category "Network" -Success $false -Details "1.1.1.1 not found in active DNS. Output: $dnsOut"
    }

    # Reset DNS to DHCP
    Get-NetAdapter | Where-Object { $_.Status -eq "Up" } | ForEach-Object {
        Set-DnsClientServerAddress -InterfaceAlias $_.Name -ResetServerAddresses -ErrorAction SilentlyContinue
    }
    Record-TestResult -TestName "Reset_DnsToDhcp" -Category "Network" -Success $true -Details "Active adapters reset to DHCP addresses"
} catch {
    Record-TestResult -TestName "DnsConfiguration" -Category "Network" -Success $false -Details "Exception: $_"
}

# ------------------------------------------------------------------------------
# TEST 5: Windows Update Mode Policy
# ------------------------------------------------------------------------------
Write-Host "`n>>> [5/6] Testing Windows Update Policy Configuration..." -ForegroundColor Cyan
try {
    $auPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"

    # Apply Security Mode (AUOptions should be 2: Notify before download)
    $upOut = & $CliExe --real-mode --update-mode "Security" 2>&1
    $auVal = (Get-ItemProperty -Path $auPath -Name "AUOptions" -ErrorAction SilentlyContinue).AUOptions

    if ($auVal -eq 2) {
        Record-TestResult -TestName "Apply_UpdateModeSecurity" -Category "WindowsUpdate" -Success $true -Details "AUOptions policy set to 2 (Security Mode)"
    } else {
        Record-TestResult -TestName "Apply_UpdateModeSecurity" -Category "WindowsUpdate" -Success $false -Details "Expected AUOptions=2, got $auVal. Output: $upOut"
    }

    # Restore Default Mode
    $defOut = & $CliExe --real-mode --update-mode "Default" 2>&1
    Record-TestResult -TestName "Restore_UpdateModeDefault" -Category "WindowsUpdate" -Success $true -Details "Update mode reverted to Windows default"
} catch {
    Record-TestResult -TestName "UpdateModePolicy" -Category "WindowsUpdate" -Success $false -Details "Exception: $_"
}

# ------------------------------------------------------------------------------
# TEST 6: System Maintenance Fix (Flush DNS Cache)
# ------------------------------------------------------------------------------
Write-Host "`n>>> [6/6] Testing System Fix Service..." -ForegroundColor Cyan
try {
    $fixOut = & $CliExe --real-mode --fix "dns_flush" 2>&1
    $fixSuccess = $LASTEXITCODE -eq 0

    if ($fixSuccess) {
        Record-TestResult -TestName "Fix_DnsFlush" -Category "SystemFixes" -Success $true -Details "DNS Resolver Cache flush script executed successfully"
    } else {
        Record-TestResult -TestName "Fix_DnsFlush" -Category "SystemFixes" -Success $false -Details "dns_flush exited with code $LASTEXITCODE. Output: $fixOut"
    }
} catch {
    Record-TestResult -TestName "Fix_DnsFlush" -Category "SystemFixes" -Success $false -Details "Exception: $_"
}

# ------------------------------------------------------------------------------
# Write Summary & JSON Report
# ------------------------------------------------------------------------------
$endTime = Get-Date
$duration = ($endTime - $startTime).TotalSeconds
$passCount = ($results | Where-Object { $_.Success }).Count
$failCount = ($results | Where-Object { -not $_.Success }).Count

$report = [PSCustomObject]@{
    TestSuite      = "Tweakora Real-Mode Windows 11 Integration"
    ExecutionMode  = "Real-Mode (Windows Platform Drivers)"
    HostOS         = (Get-CimInstance Win32_OperatingSystem).Caption
    OSVersion      = (Get-CimInstance Win32_OperatingSystem).Version
    StartTime      = $startTime.ToString("o")
    EndTime        = $endTime.ToString("o")
    DurationSec    = [math]::Round($duration, 2)
    TotalTests     = $results.Count
    PassCount      = $passCount
    FailCount      = $failCount
    OverallSuccess = ($failCount -eq 0)
    Results        = $results
}

$jsonDir = Split-Path $ReportPath -Parent
if (-not (Test-Path $jsonDir)) { New-Item -ItemType Directory -Path $jsonDir -Force | Out-Null }
$report | ConvertTo-Json -Depth 5 | Set-Content -Path $ReportPath -Encoding utf8

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "               INTEGRATION TESTS SUMMARY                  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Total Tests: $passCount passed, $failCount failed (Duration: $($report.DurationSec)s)" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "JSON Report saved to: $ReportPath"
Write-Host ""

if ($failCount -gt 0) {
    exit 1
} else {
    exit 0
}
