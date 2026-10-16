<#
.SYNOPSIS
    Builds the WinVelo portable release (ready to run, no installation, no .NET needed).
.DESCRIPTION
    Produces dist/WinVelo-v<Version>-portable/ containing:
      WinVelo.exe          - the app (self-contained single file, .NET runtime included)
      winvelo-cli.exe      - the command-line tool (self-contained single file)
      Catalog/             - tweak and preset definitions (plain JSON, readable and editable)
      LICENSE, README.md, THIRD-PARTY-NOTICES.md
    Then zips it to dist/WinVelo-v<Version>-portable.zip and writes dist/SHA256SUMS.txt.
.EXAMPLE
    .\build\publish.ps1
    .\build\publish.ps1 -Version 0.9.1-beta
#>

[CmdletBinding()]
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}

$running = Get-Process -Name "WinVelo", "winvelo-cli" -ErrorAction SilentlyContinue
if ($running) {
    throw "WinVelo or winvelo-cli is running (PID $(($running.Id) -join ', ')). Close it before publishing."
}

$distDir     = Join-Path $repoRoot "dist"
$name        = "WinVelo-v$Version-portable"
$outDir      = Join-Path $distDir $name
$zipPath     = Join-Path $distDir "$name.zip"
$tempCli     = Join-Path $distDir "_temp_cli"
$shaSumsFile = Join-Path $distDir "SHA256SUMS.txt"

$singleFileArgs = @(
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:SatelliteResourceLanguages=en",
    "-p:DebugType=none",
    "-p:GenerateDocumentationFile=false"
)

Write-Host "=== WinVelo portable release v$Version ===" -ForegroundColor Cyan

Write-Host "`n[1/5] Cleaning old output..." -ForegroundColor Yellow
foreach ($p in @($outDir, $tempCli)) { if (Test-Path $p) { Remove-Item -Recurse -Force $p } }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "`n[2/5] Publishing WinVelo.exe..." -ForegroundColor Yellow
dotnet publish src/Tweakora.App/Tweakora.App.csproj @singleFileArgs -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for the app." }

Write-Host "`n[3/5] Publishing winvelo-cli.exe..." -ForegroundColor Yellow
dotnet publish src/Tweakora.Cli/Tweakora.Cli.csproj @singleFileArgs -o $tempCli
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for the CLI." }
Copy-Item (Join-Path $tempCli "winvelo-cli.exe") $outDir -Force
Remove-Item -Recurse -Force $tempCli

foreach ($doc in @("LICENSE", "README.md", "THIRD-PARTY-NOTICES.md")) {
    $src = Join-Path $repoRoot $doc
    if (-not (Test-Path $src)) { throw "Required file '$doc' is missing from the repo root." }
    Copy-Item $src (Join-Path $outDir $doc) -Force
}

Write-Host "`n[4/5] Checking the package..." -ForegroundColor Yellow
$required = @("WinVelo.exe", "winvelo-cli.exe", "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md", "Catalog\Tweaks", "Catalog\Presets")
foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $outDir $r))) { throw "Package check failed: '$r' is missing." }
}
if (Test-Path (Join-Path $outDir "Catalog\Catalog")) { throw "Package check failed: nested Catalog\Catalog folder." }

$allowedRoot = @("WinVelo.exe", "winvelo-cli.exe", "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md", "Catalog")
$extra = Get-ChildItem $outDir | Where-Object { $allowedRoot -notcontains $_.Name }
if ($extra) {
    throw "Package check failed: unexpected files in the package root: $(($extra.Name) -join ', '). Only the exe files, Catalog and the license/readme files are allowed."
}
$stray = Get-ChildItem $outDir -Recurse -File -Include *.dll, *.pdb, *.deps.json, *.runtimeconfig.json
if ($stray) { throw "Package check failed: found $(($stray.Name) -join ', ')." }
Write-Host "  [OK] Package contains only the two exe files, Catalog and the license/readme files." -ForegroundColor Green

Write-Host "`n[5/5] Zipping and computing SHA-256..." -ForegroundColor Yellow
Compress-Archive -Path $outDir -DestinationPath $zipPath -Force
$zipHash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash.ToUpperInvariant()
$exeHash = (Get-FileHash -Algorithm SHA256 (Join-Path $outDir "WinVelo.exe")).Hash.ToUpperInvariant()

$legacy = ""
if (Test-Path $shaSumsFile) {
    $existing = Get-Content $shaSumsFile -Raw
    $idx = $existing.IndexOf("# Legacy Releases")
    if ($idx -ge 0) { $legacy = $existing.Substring($idx).Trim() }
}
$lines = @("# WinVelo Releases", "$zipHash  $name.zip", "$exeHash  $name/WinVelo.exe")
if ($legacy) { $lines += @("", $legacy) }
[System.IO.File]::WriteAllText($shaSumsFile, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($false)))

$exeMb = [Math]::Round((Get-Item (Join-Path $outDir "WinVelo.exe")).Length / 1MB, 1)
$zipMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Folder : $outDir"
Write-Host "Zip    : $zipPath ($zipMb MB)"
Write-Host "Exe    : WinVelo.exe ($exeMb MB)"
Write-Host "SHA256 : $zipHash"
