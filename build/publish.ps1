<#
.SYNOPSIS
    Reproducible release packaging script for WinVelo.
.DESCRIPTION
    Cleans output directories, publishes App and CLI for Portable (self-contained)
    and Small (framework-dependent), validates package integrity (no PDBs, single Catalog,
    required docs present), archives to zip, and calculates SHA-256 checksums in dist/SHA256SUMS.txt.
#>

[CmdletBinding()]
param(
    [string]$Version = "0.9.0-beta"
)

$ErrorActionPreference = "Stop"

function Assert-PackageValid {
    param([string]$targetDir, [string]$name)

    Write-Host "  Auditing [$name]..." -ForegroundColor Cyan

    # Check 1: Must fail if any .pdb is present
    $pdbs = Get-ChildItem -Path $targetDir -Recurse -Filter "*.pdb"
    if ($pdbs.Count -gt 0) {
        $pdbList = ($pdbs | Select-Object -ExpandProperty Name) -join ', '
        throw "Validation Failed for [$name]: Found $($pdbs.Count) PDB symbol file(s): $pdbList"
    }

    # Check 2: Must fail if duplicate Catalog/Catalog exists
    $nestedCatalog = Join-Path $targetDir "Catalog\Catalog"
    if (Test-Path $nestedCatalog) {
        throw "Validation Failed for [$name]: Duplicate nested 'Catalog\Catalog' directory detected!"
    }

    # Check 3: Must fail if Catalog/ is missing at package root
    $rootCatalog = Join-Path $targetDir "Catalog"
    if (-not (Test-Path $rootCatalog)) {
        throw "Validation Failed for [$name]: Missing 'Catalog' directory at package root!"
    }

    # Check 4: Must verify required files exist
    $requiredFiles = @("WinVelo.exe", "winvelo-cli.exe", "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md")
    foreach ($f in $requiredFiles) {
        $p = Join-Path $targetDir $f
        if (-not (Test-Path $p)) {
            throw "Validation Failed for [$name]: Missing required file '$f'!"
        }
    }

    Write-Host "  [OK] [$name] passed all integrity checks." -ForegroundColor Green
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

# Safety check: ensure running processes do not lock files
$runningProcesses = Get-Process -Name "WinVelo", "winvelo-cli" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    $procIds = ($runningProcesses | Select-Object -ExpandProperty Id) -join ', '
    Write-Error "WinVelo or winvelo-cli is currently running (PID(s): $procIds). Please close them before publishing."
    exit 1
}

$distDir = Join-Path $repoRoot "dist"
$portableDir = Join-Path $distDir "WinVelo-v$Version-win-x64-portable"
$smallDir = Join-Path $distDir "WinVelo-v$Version-win-x64-small"
$portableZip = Join-Path $distDir "WinVelo-v$Version-win-x64-portable.zip"
$smallZip = Join-Path $distDir "WinVelo-v$Version-win-x64-small.zip"
$shaSumsFile = Join-Path $distDir "SHA256SUMS.txt"

Write-Host "=== WinVelo Reproducible Release Publisher ===" -ForegroundColor Cyan
Write-Host "Repository root: $repoRoot"
Write-Host "Output dist dir: $distDir"

# 1. Clean output directories
Write-Host "`n[1/6] Cleaning existing staging and package targets..." -ForegroundColor Yellow
if (Test-Path $portableDir) { Remove-Item -Recurse -Force $portableDir }
if (Test-Path $smallDir) { Remove-Item -Recurse -Force $smallDir }
if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
if (Test-Path $smallZip) { Remove-Item -Force $smallZip }

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

# 2. Publish Portable (Self-Contained)
Write-Host "`n[2/6] Publishing Portable Package (Self-Contained)..." -ForegroundColor Yellow
dotnet publish src/Tweakora.App/Tweakora.App.csproj -c Release -r win-x64 --self-contained true -o $portableDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for portable App." }

$tempCliPortable = Join-Path $distDir "_temp_cli_portable"
if (Test-Path $tempCliPortable) { Remove-Item -Recurse -Force $tempCliPortable }

dotnet publish src/Tweakora.Cli/Tweakora.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $tempCliPortable
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for portable CLI." }

Copy-Item (Join-Path $tempCliPortable "winvelo-cli.exe") $portableDir -Force
Remove-Item -Recurse -Force $tempCliPortable

# 3. Publish Small (Framework-Dependent)
Write-Host "`n[3/6] Publishing Small Package (Framework-Dependent)..." -ForegroundColor Yellow
dotnet publish src/Tweakora.App/Tweakora.App.csproj -c Release -r win-x64 --self-contained false -o $smallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for small App." }

dotnet publish src/Tweakora.Cli/Tweakora.Cli.csproj -c Release -r win-x64 --self-contained false -o $smallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for small CLI." }

# 4. Copy Required Documents
Write-Host "`n[4/6] Copying licenses and documentation..." -ForegroundColor Yellow
$docsToCopy = @("LICENSE", "README.md", "THIRD-PARTY-NOTICES.md")
foreach ($doc in $docsToCopy) {
    $src = Join-Path $repoRoot $doc
    if (-not (Test-Path $src)) {
        throw "Required document '$doc' is missing from repo root."
    }
    Copy-Item $src (Join-Path $portableDir $doc) -Force
    Copy-Item $src (Join-Path $smallDir $doc) -Force
}

# 5. Strict Package Integrity Validation
Write-Host "`n[5/6] Validating package integrity..." -ForegroundColor Yellow
Assert-PackageValid $portableDir "WinVelo Portable (win-x64)"
Assert-PackageValid $smallDir "WinVelo Small (win-x64)"

# 6. Archive to Zip and Generate Checksums
Write-Host "`n[6/6] Archiving release packages and computing SHA-256 sums..." -ForegroundColor Yellow

Compress-Archive -Path "$portableDir\*" -DestinationPath $portableZip -Force
Compress-Archive -Path "$smallDir\*" -DestinationPath $smallZip -Force

$portableHash = (Get-FileHash -Algorithm SHA256 $portableZip).Hash.ToUpperInvariant()
$smallHash = (Get-FileHash -Algorithm SHA256 $smallZip).Hash.ToUpperInvariant()

$legacySection = @(
    "# Legacy Releases (Tweakora Pre-1.0)",
    "9D1BCE974E65A261134283DB7C1FBD80CB0ADEF5ED225B32B6789E9EFDA7669A  Tweakora-v0.1.0-win-x64-portable.zip",
    "B40DF8F97B5155D693711AF505B896341B3C369E3C9D87AF3AEF040BC20C3D80  Tweakora-v0.1.0-win-x64-small.zip",
    "0F8FF42AEB26356182249A3B5D7C9F0ED114B0921672E6A21B4922CEF1CE143F  Tweakora-v0.2.0-win-x64-portable.zip",
    "E41169D77FA1CCF16DC4E2AE9578C2EFC37EF2E0995EF4C27C7B4A504670C0B4  Tweakora-v0.2.0-win-x64-small.zip",
    "231A5BB2EC3B31F9DD8B05488E9770D00392B925F2A8CAAC55D14FC55A37E4EA  Tweakora-v1.0.0-rc1-win-x64-portable.zip",
    "8482FCBC36A1C5E17FB757C5E31AD06A3483283EE9FCF1CD09D2701F6040C930  Tweakora-v1.0.0-rc1-win-x64-small.zip"
) -join [Environment]::NewLine

if (Test-Path $shaSumsFile) {
    $existing = Get-Content $shaSumsFile -Raw
    $idx = $existing.IndexOf("# Legacy Releases")
    if ($idx -ge 0) {
        $legacySection = $existing.Substring($idx).Trim()
    }
}

$shaContent = @(
    "# WinVelo Production Releases",
    "$portableHash  WinVelo-v$Version-win-x64-portable.zip",
    "$smallHash  WinVelo-v$Version-win-x64-small.zip",
    "",
    $legacySection
) -join [Environment]::NewLine

[System.IO.File]::WriteAllText($shaSumsFile, $shaContent + [Environment]::NewLine, [System.Text.Encoding]::UTF8)

$portableSizeMb = [Math]::Round((Get-Item $portableZip).Length / 1MB, 2)
$smallSizeMb = [Math]::Round((Get-Item $smallZip).Length / 1MB, 2)

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " WinVelo Release v$Version Successfully Built & Packaged!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host "Portable Package: $portableZip"
Write-Host "  Size:   $portableSizeMb MB"
Write-Host "  SHA256: $portableHash"
Write-Host "`nSmall Package:    $smallZip"
Write-Host "  Size:   $smallSizeMb MB"
Write-Host "  SHA256: $smallHash"
Write-Host "`nChecksums updated in $shaSumsFile"

