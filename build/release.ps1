<#
.SYNOPSIS
    One command to verify WinVelo and produce a GitHub-ready release folder.
.DESCRIPTION
    1. Builds the solution (Release).
    2. Validates the tweak catalog and checks that the committed presets match the generator.
    3. Runs the unit tests.
    4. Retakes the README screenshots (English and Arabic) in test mode (no system changes). Skip with -SkipScreenshots.
    5. Builds the portable package (build\publish.ps1).
    6. Creates dist\WinVelo-v<Version>-GitHub\ with:
         source\     the open-source repository files (tracked files only, no build output)
         release\    WinVelo-v<Version>-portable.zip, SHA256SUMS.txt, RELEASE_NOTES.md
    Any failing step stops the script.
.EXAMPLE
    .\build\release.ps1
#>

[CmdletBinding()]
param(
    [switch]$SkipScreenshots,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

function Step([string]$text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Run([string]$what, [scriptblock]$cmd) {
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

[xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
$Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "WinVelo release v$Version" -ForegroundColor Green

$running = Get-Process -Name "WinVelo", "winvelo-cli" -ErrorAction SilentlyContinue
if ($running) { throw "Close WinVelo / winvelo-cli first (PID $(($running.Id) -join ', '))." }

Step "1/6 Build"
Run "dotnet build" { dotnet build WinVelo.sln -c Release --nologo -v minimal }

Step "2/6 Catalog and presets"
Run "Catalog validator" { dotnet run --project tools/CatalogValidator/CatalogValidator.csproj -c Release --no-build }
Run "Preset generator" { dotnet run --project tools/PresetGenerator/PresetGenerator.csproj -c Release --no-build }
$presetDiff = git status --porcelain -- Catalog/Presets
if ($presetDiff) {
    throw "The generated presets differ from the committed files:`n$presetDiff`nReview with 'git diff Catalog/Presets', commit them, and run this script again."
}
Write-Host "[OK] Presets match the generator." -ForegroundColor Green

if (-not $SkipTests) {
    Step "3/6 Unit tests"
    Run "Unit tests" { dotnet test src/WinVelo.Tests/WinVelo.Tests.csproj -c Release --no-build --nologo }
} else {
    Write-Host "`n(Skipping tests)" -ForegroundColor Yellow
}

if (-not $SkipScreenshots) {
    Step "4/6 Screenshots (test mode, no system changes)"
    $appExe = Join-Path $repoRoot "src\WinVelo.App\bin\Release\net10.0-windows\WinVelo.exe"
    if (-not (Test-Path $appExe)) { throw "Built app not found at $appExe" }
    $shotDir = Join-Path $repoRoot "docs\screenshots"
    $p = Start-Process -FilePath $appExe -ArgumentList @("--test-mode", "--capture-screenshots", "`"$shotDir`"") -PassThru -Wait
    if ($null -ne $p.ExitCode -and $p.ExitCode -ne 0) { throw "Screenshot capture failed (exit code $($p.ExitCode))." }
    $shotDirAr = Join-Path $shotDir "ar"
    $pAr = Start-Process -FilePath $appExe -ArgumentList @("--test-mode", "--lang", "ar", "--capture-screenshots", "`"$shotDirAr`"") -PassThru -Wait
    if ($null -ne $pAr.ExitCode -and $pAr.ExitCode -ne 0) { throw "Arabic screenshot capture failed (exit code $($pAr.ExitCode))." }
    Write-Host "[OK] Screenshots updated in docs\screenshots. Commit them if they changed." -ForegroundColor Green
} else {
    Write-Host "`n(Skipping screenshots)" -ForegroundColor Yellow
}

Step "5/6 Portable package"
& (Join-Path $PSScriptRoot "publish.ps1") -Version $Version

Step "6/6 GitHub folder"
$name       = "WinVelo-v$Version"
$outRoot    = Join-Path $repoRoot "dist\$name-GitHub"
$sourceDir  = Join-Path $outRoot "source"
$releaseDir = Join-Path $outRoot "release"
if (Test-Path $outRoot) { Remove-Item -Recurse -Force $outRoot }
New-Item -ItemType Directory -Path $sourceDir, $releaseDir -Force | Out-Null

# Source: every file git tracks (with your current edits), nothing ignored, no build output.
$files = git ls-files --cached --others --exclude-standard
foreach ($f in $files) {
    $src = Join-Path $repoRoot $f
    if (-not (Test-Path $src -PathType Leaf)) { continue }
    $dst = Join-Path $sourceDir $f
    New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
    Copy-Item $src $dst -Force
}
$bad = Get-ChildItem $sourceDir -Recurse -File -Include *.exe, *.dll, *.pdb, *.zip
if ($bad) { throw "Source folder contains build output: $(($bad.Name) -join ', ')" }

$zipName = "$name-portable.zip"
Copy-Item (Join-Path $repoRoot "dist\$zipName") $releaseDir -Force
$zipHash = (Get-FileHash -Algorithm SHA256 (Join-Path $releaseDir $zipName)).Hash.ToUpperInvariant()
[System.IO.File]::WriteAllText((Join-Path $releaseDir "SHA256SUMS.txt"), "$zipHash  $zipName`r`n", (New-Object System.Text.UTF8Encoding($false)))

$notes = @"
## WinVelo v$Version

A transparent, open-source tweak tool for Windows 10 and 11. See exactly what every setting changes, apply it, and undo it.

### Download
**$zipName**: unzip it and run ``WinVelo.exe``. You don't need to install anything or download .NET. Keep the ``Catalog`` folder next to the exe.

> WinVelo isn't code-signed yet, so Windows SmartScreen may warn you. Click **More info**, then **Run anyway**.

SHA-256: ``$zipHash``

### Highlights
- 140 tweaks, each showing its exact registry/service changes, risk, side effects and source
- Presets (Recommended, Privacy, AI & Copilot, Clean UI, Performance, Gaming) with a preview before applying
- Rollback journal with undo for single tweaks, sessions or everything
- App installer (winget), startup, services, cleaner, DNS, fixes, updates, optional features, restore points
- English and Arabic interface (Settings > Language)
- No telemetry, ads or update checks

See [CHANGELOG.md](https://github.com/Ziadtareks/WinVelo/blob/main/CHANGELOG.md) for the full list of changes.
"@
[System.IO.File]::WriteAllText((Join-Path $releaseDir "RELEASE_NOTES.md"), $notes, (New-Object System.Text.UTF8Encoding($false)))

$srcCount = (Get-ChildItem $sourceDir -Recurse -File).Count
Write-Host "`n========================================" -ForegroundColor Green
Write-Host " WinVelo v$Version is ready" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Folder : $outRoot"
Write-Host "  source\   $srcCount files (open-source repository)"
Write-Host "  release\  $zipName, SHA256SUMS.txt, RELEASE_NOTES.md"
Write-Host "SHA256 : $zipHash"
