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
   - Sign the exe files, only when signing is turned on (see [Code signing](#code-signing-off-until-a-certificate-exists)).
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

---

## Code signing (off until a certificate exists)

Releases are **not code-signed** yet, and nothing in the build pretends otherwise: the release notes say whether a release is signed. Users can check a download with the `.sha256` file and the build attestation (README, *Verify your download*).

The release workflow already has a signing step between `build\publish.ps1 -SkipZip` and `-PackageOnly`, so the hashes and the attestation always describe the signed files. It stays off until the repository variable `WINVELO_SIGNING` is `signpath`.

To turn it on with [SignPath Foundation](https://signpath.org) (free code signing for open-source projects that build in CI):

1. Apply at <https://signpath.org> and create the project in SignPath (an artifact configuration that signs `WinVelo.exe` and `winvelo-cli.exe`, and a signing policy).
2. In GitHub, **Settings › Secrets and variables › Actions**, add the secrets `SIGNPATH_API_TOKEN` and `SIGNPATH_ORGANIZATION_ID`, and the variables `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG`.
3. Set the variable `WINVELO_SIGNING` to `signpath`.
4. Before the first signed release, check the [SignPath GitHub action](https://github.com/SignPath/github-action-submit-signing-request) README; its version and inputs may have changed.

To sign a local build instead: `.\build\publish.ps1 -SkipZip`, sign the two exe files in `dist\WinVelo-v<version>-portable\`, then `.\build\publish.ps1 -PackageOnly`. A self-signed certificate only helps for your own tests; SmartScreen still warns on other PCs.
