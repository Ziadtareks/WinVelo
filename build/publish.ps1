<#
.SYNOPSIS
    Builds the WinVelo portable release (ready to run, no installation, no .NET needed).
.DESCRIPTION
    Produces dist/WinVelo-v<Version>-portable/ containing:
      WinVelo.exe          - the app (self-contained single file, .NET runtime included)
      winvelo-cli.exe      - the command-line tool (self-contained single file)
      LICENSE, README.md, THIRD-PARTY-NOTICES.md
    Then zips it to dist/WinVelo-v<Version>-portable.zip and writes the SHA-256 hashes:
      dist/WinVelo-v<Version>-portable.zip.sha256   - one line "<hash>  <file name>" (sha256sum format)
      dist/SHA256SUMS.txt                           - the zip and both exe files

    The release workflow signs the exe files between the two stages:
      .\build\publish.ps1 -SkipZip       # build dist/WinVelo-v<Version>-portable/
      (sign WinVelo.exe and winvelo-cli.exe)
      .\build\publish.ps1 -PackageOnly   # zip the folder and write the hash files
.EXAMPLE
    .\build\publish.ps1
    .\build\publish.ps1 -Version 1.0.2
#>

[CmdletBinding()]
param(
    [string]$Version = "",
    # Build the folder but do not zip it (so the exe files can be signed first).
    [switch]$SkipZip,
    # Skip the build and only zip an existing dist/WinVelo-v<Version>-portable/ folder and write its hashes.
    [switch]$PackageOnly
)

if ($SkipZip -and $PackageOnly) { throw "Use either -SkipZip or -PackageOnly, not both." }

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}

$running = Get-Process -Name "WinVelo", "winvelo-cli" -ErrorAction SilentlyContinue
if ($running -and -not $PackageOnly) {
    throw "WinVelo or winvelo-cli is running (PID $(($running.Id) -join ', ')). Close it before publishing."
}

$distDir     = Join-Path $repoRoot "dist"
$name        = "WinVelo-v$Version-portable"
$outDir      = Join-Path $distDir $name
$zipPath     = Join-Path $distDir "$name.zip"
$tempCli     = Join-Path $distDir "_temp_cli"
$shaSumsFile = Join-Path $distDir "SHA256SUMS.txt"
$zipHashFile = "$zipPath.sha256"

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

function Write-Utf8NoBom([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

if (-not $PackageOnly) {

Write-Host "`n[1/5] Cleaning old output..." -ForegroundColor Yellow
foreach ($p in @($outDir, $tempCli)) { if (Test-Path $p) { Remove-Item -Recurse -Force $p } }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "`n[2/5] Publishing WinVelo.exe..." -ForegroundColor Yellow
dotnet publish src/WinVelo.App/WinVelo.App.csproj @singleFileArgs -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for the app." }

Write-Host "`n[3/5] Publishing winvelo-cli.exe..." -ForegroundColor Yellow
dotnet publish src/WinVelo.Cli/WinVelo.Cli.csproj @singleFileArgs -o $tempCli
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for the CLI." }
Copy-Item (Join-Path $tempCli "winvelo-cli.exe") $outDir -Force
Remove-Item -Recurse -Force $tempCli

foreach ($doc in @("LICENSE", "README.md", "THIRD-PARTY-NOTICES.md")) {
    $src = Join-Path $repoRoot $doc
    if (-not (Test-Path $src)) { throw "Required file '$doc' is missing from the repo root." }
    Copy-Item $src (Join-Path $outDir $doc) -Force
}

Write-Host "`n[4/5] Checking the package..." -ForegroundColor Yellow
$required = @("WinVelo.exe", "winvelo-cli.exe", "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md")
foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $outDir $r))) { throw "Package check failed: '$r' is missing." }
}

$allowedRoot = @("WinVelo.exe", "winvelo-cli.exe", "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md")
$extra = Get-ChildItem $outDir | Where-Object { $allowedRoot -notcontains $_.Name }
if ($extra) {
    throw "Package check failed: unexpected files in the package root: $(($extra.Name) -join ', '). Only the exe files and the license/readme files are allowed. The catalog is built into the exe files."
}
$stray = Get-ChildItem $outDir -Recurse -File -Include *.dll, *.pdb, *.deps.json, *.runtimeconfig.json
if ($stray) { throw "Package check failed: found $(($stray.Name) -join ', ')." }
Write-Host "  [OK] Package contains only the two exe files and the license/readme files." -ForegroundColor Green

if ($SkipZip) {
    Write-Host "`n=== Folder ready (not zipped): $outDir ===" -ForegroundColor Green
    Write-Host "Sign the exe files, then run: .\build\publish.ps1 -Version $Version -PackageOnly"
    return
}

} # end of build stage

if (-not (Test-Path (Join-Path $outDir "WinVelo.exe"))) {
    throw "Nothing to package: $outDir\WinVelo.exe is missing. Run .\build\publish.ps1 -SkipZip first."
}

Write-Host "`n[5/5] Zipping and computing SHA-256..." -ForegroundColor Yellow
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path $outDir -DestinationPath $zipPath -Force
$zipHash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash.ToLowerInvariant()
$exeHash = (Get-FileHash -Algorithm SHA256 (Join-Path $outDir "WinVelo.exe")).Hash.ToLowerInvariant()
$cliHash = (Get-FileHash -Algorithm SHA256 (Join-Path $outDir "winvelo-cli.exe")).Hash.ToLowerInvariant()

# sha256sum format ("<hash>  <file>"), so both Get-FileHash and `sha256sum -c` can check it.
Write-Utf8NoBom $zipHashFile "$zipHash  $name.zip`n"
$lines = @("$zipHash  $name.zip", "$exeHash  $name/WinVelo.exe", "$cliHash  $name/winvelo-cli.exe")
Write-Utf8NoBom $shaSumsFile (($lines -join "`n") + "`n")

$exeMb = [Math]::Round((Get-Item (Join-Path $outDir "WinVelo.exe")).Length / 1MB, 1)
$zipMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Folder : $outDir"
Write-Host "Zip    : $zipPath ($zipMb MB)"
Write-Host "Exe    : WinVelo.exe ($exeMb MB)"
Write-Host "SHA256 : $zipHash"
Write-Host "Hashes : $zipHashFile, $shaSumsFile"
