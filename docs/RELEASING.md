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
2. Select the **Release Pipeline** workflow run triggered by your tag `v1.0.0`.
3. The workflow will:
   - Restore dependencies and validate the catalog schema.
   - Compile Release binaries.
   - Run the test suite.
   - Check for code signing secrets (or skip cleanly if unsigned).
   - Package `WinVelo-v1.0.0-portable.zip` (single-file `WinVelo.exe` + `winvelo-cli.exe`, `Catalog/`, license files).
   - Calculate SHA-256 hashes and update `dist/SHA256SUMS.txt`.
   - Cryptographically attest build provenance via GitHub OIDC (`actions/attest-build-provenance`).
   - Create a **Draft** GitHub Release with all assets attached.

---

## Step 4: Review and Publish the Draft Release

1. Go to `https://github.com/Ziadtareks/WinVelo/releases`.
2. Locate the **Draft** release titled `WinVelo v1.0.0`.
3. Verify that the two release artifacts are attached:
   - `WinVelo-v1.0.0-portable.zip`
   - `SHA256SUMS.txt`
4. Confirm that the SHA-256 hashes in `SHA256SUMS.txt` match your local build output:
   - The zip hash printed at the end of `build\publish.ps1`
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
