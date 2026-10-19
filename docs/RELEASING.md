# WinVelo Release Guide

This document provides step-by-step instructions for publishing an official release of **WinVelo**, verifying the build provenance attestation, and (optionally) submitting it to Windows Package Manager.

---

## Prerequisites

- [Git](https://git-scm.com/) installed and authenticated with push access to `Ziadtareks/WinVelo`.
- [GitHub CLI (`gh`)](https://cli.github.com/) installed and authenticated (`gh auth login`).
- [Windows Package Manager Manifest Creator (`wingetcreate`)](https://github.com/microsoft/winget-create) installed (`winget install Microsoft.WingetCreate`).

---

## Step 1: Pre-Flight Verification

Ensure the local repository is clean and all verification gates pass:

```powershell
# 1. Ensure working tree is clean
git status

# 2. Run all unit tests
dotnet test src/WinVelo.Tests/WinVelo.Tests.csproj -c Debug

# 3. Run UI tests in safe simulation test mode
dotnet test src/WinVelo.UiTests/WinVelo.UiTests.csproj -c Debug

# 4. Verify Catalog Schema and Evidence
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj

# 5. Verify local release packaging script
.\build\publish.ps1 -Version "1.0.0"
```

---

## Step 2: Create and Push Git Tag

Create an annotated git tag corresponding to the release version and push it to GitHub:

```bash
# Create annotated tag
git tag -a v1.0.0 -m "WinVelo v1.0.0 release"

# Push tag to trigger CI release workflow
git push origin v1.0.0
```

---

## Step 3: Monitor CI Pipeline Execution

1. Navigate to `https://github.com/Ziadtareks/WinVelo/actions`.
2. Select the **Release** workflow run triggered by your tag `v1.0.0`.
3. The workflow will:
   - Check that the tag matches `<Version>` in `Directory.Build.props`.
   - Restore, build, validate the catalog and the README tweak count, and run the unit tests.
   - Build the portable folder (`build\publish.ps1 -SkipZip`).
   - Sign the exe files, only when signing is turned on (see [SIGNING.md](SIGNING.md)).
   - Zip the folder and write `WinVelo-v1.0.0-portable.zip.sha256` and `SHA256SUMS.txt` (`build\publish.ps1 -PackageOnly`).
   - Attest build provenance (`actions/attest-build-provenance`).
   - Create a **Draft** GitHub Release with the zip and both hash files.

---

## Step 4: Review and Publish the Draft Release

1. Go to `https://github.com/Ziadtareks/WinVelo/releases`.
2. Locate the **Draft** release titled `WinVelo v1.0.0`.
3. Verify that the three release files are attached:
   - `WinVelo-v1.0.0-portable.zip`
   - `WinVelo-v1.0.0-portable.zip.sha256`
   - `SHA256SUMS.txt`
4. Download the zip and check it as described in the README (*Verify your download*). Do not compare it with a local build: builds are not byte-for-byte reproducible, so a local zip has a different hash.
5. Click **"Publish release"**.

---

## Step 5: Verify SLSA Build Provenance Attestation

After publishing, verify the cryptographic build provenance using the GitHub CLI:

```bash
# Download the portable zip from the release
gh release download v1.0.0 --pattern "WinVelo-*-portable.zip"

# Verify cryptographic provenance against the GitHub repository
gh attestation verify WinVelo-v1.0.0-portable.zip --repo Ziadtareks/WinVelo
```

Expected output confirms the artifact was built on a trusted GitHub-hosted runner from the tagged commit:
```text
Loaded digest sha256:cec61ea... for WinVelo-v1.0.0-portable.zip
Loaded 1 attestation from GitHub API
✓ Verification succeeded!
```

---

## Step 6 (optional): Publish to Windows Package Manager (`winget`)

After the GitHub release is public, create and submit a manifest that points to the release zip:

```powershell
winget install Microsoft.WingetCreate
wingetcreate new https://github.com/Ziadtareks/WinVelo/releases/download/v1.0.0/WinVelo-v1.0.0-portable.zip
```
