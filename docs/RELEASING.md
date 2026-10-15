# WinVelo Release Guide

This document provides step-by-step instructions for publishing an official release of **WinVelo**, verifying the build provenance attestation, and submitting the package to the official Windows Package Manager (`winget-pkgs`) repository.

---

## Prerequisites

- [Git](https://git-scm.com/) installed and authenticated with push access to `winvelo/winvelo`.
- [GitHub CLI (`gh`)](https://cli.github.com/) installed and authenticated (`gh auth login`).
- [Windows Package Manager Manifest Creator (`wingetcreate`)](https://github.com/microsoft/winget-create) installed (`winget install Microsoft.WingetCreate`).

---

## Step 1: Pre-Flight Verification

Ensure the local repository is clean and all verification gates pass:

```powershell
# 1. Ensure working tree is clean
git status

# 2. Run all unit tests
dotnet test src/Tweakora.Tests/Tweakora.Tests.csproj -c Debug

# 3. Run UI tests in safe simulation test mode
dotnet test src/Tweakora.UiTests/Tweakora.UiTests.csproj -c Debug

# 4. Verify Catalog Schema and Evidence
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj

# 5. Verify local release packaging script
.\build\publish.ps1 -Version "0.9.0-beta"
```

---

## Step 2: Create and Push Git Tag

Create an annotated git tag corresponding to the release version and push it to GitHub:

```bash
# Create annotated tag
git tag -a v0.9.0-beta -m "WinVelo v0.9.0-beta release"

# Push tag to trigger CI release workflow
git push origin v0.9.0-beta
```

---

## Step 3: Monitor CI Pipeline Execution

1. Navigate to `https://github.com/winvelo/winvelo/actions`.
2. Select the **Release Pipeline** workflow run triggered by your tag `v0.9.0-beta`.
3. The workflow will:
   - Restore dependencies and validate the catalog schema.
   - Compile Release binaries.
   - Run the test suite.
   - Check for code signing secrets (or skip cleanly if unsigned).
   - Package `WinVelo-v0.9.0-beta-win-x64-portable.zip` and `WinVelo-v0.9.0-beta-win-x64-small.zip`.
   - Calculate SHA-256 hashes and update `dist/SHA256SUMS.txt`.
   - Cryptographically attest build provenance via GitHub OIDC (`actions/attest-build-provenance`).
   - Create a **Draft** GitHub Release with all assets attached.

---

## Step 4: Review and Publish the Draft Release

1. Go to `https://github.com/winvelo/winvelo/releases`.
2. Locate the **Draft** release titled `WinVelo v0.9.0-beta`.
3. Verify that the three release artifacts are attached:
   - `WinVelo-v0.9.0-beta-win-x64-portable.zip`
   - `WinVelo-v0.9.0-beta-win-x64-small.zip`
   - `SHA256SUMS.txt`
4. Confirm that the SHA-256 hashes in `SHA256SUMS.txt` match your local build output:
   - Portable: `CEC61EA16AC0E23DA94E0989D1F7B9B77422E277D178781C8B94A06E3BB21D8B`
   - Small: `1627628D267FADA729C94AA1BDD56BD9E91AD3AAAAEA6D828AE8F12C617E419C`
5. Click **"Publish release"**.

---

## Step 5: Verify SLSA Build Provenance Attestation

After publishing, verify the cryptographic build provenance using the GitHub CLI:

```bash
# Download the portable zip from the release
gh release download v0.9.0-beta --pattern "WinVelo-*-portable.zip"

# Verify cryptographic provenance against the GitHub repository
gh attestation verify WinVelo-v0.9.0-beta-win-x64-portable.zip --repo winvelo/winvelo
```

Expected output confirms the artifact was built on a trusted GitHub-hosted runner from the tagged commit:
```text
Loaded digest sha256:cec61ea... for WinVelo-v0.9.0-beta-win-x64-portable.zip
Loaded 1 attestation from GitHub API
✓ Verification succeeded!
```

---

## Step 6: Submit Package to Windows Package Manager (`winget-pkgs`)

WinVelo manifests are pre-generated under `manifests/w/WinVelo/WinVelo/0.9.0-beta/`.

### Option A: Using `wingetcreate` CLI
```powershell
# Submit the pre-generated manifests directory
wingetcreate submit manifests\w\WinVelo\WinVelo\0.9.0-beta
```
*`wingetcreate` will prompt for a GitHub Personal Access Token (PAT) with `public_repo` scope and open a Pull Request against `microsoft/winget-pkgs`.*

### Option B: Manual Fork & Pull Request
1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
2. Copy `manifests/w/WinVelo/WinVelo/0.9.0-beta/` into `manifests/w/WinVelo/WinVelo/0.9.0-beta/` in your fork.
3. Validate locally with winget:
   ```powershell
   winget validate --manifest manifests\w\WinVelo\WinVelo\0.9.0-beta
   ```
4. Commit and submit a PR to `microsoft/winget-pkgs`.
