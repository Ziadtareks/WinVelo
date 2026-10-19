# Code signing and release integrity

## Current status: not signed

WinVelo releases are **not code-signed** yet. A trusted Authenticode certificate costs money and needs an identity check, and the project does not have one. Nothing in the build pretends otherwise: the release notes say whether a release is signed.

Because the exe files are unsigned, Windows SmartScreen may show *"Windows protected your PC"*. Check the download first (below), then click **More info** and **Run anyway**.

## How to check a download without a signature

The [release workflow](../.github/workflows/release.yml) builds every release in GitHub Actions (from v1.0.2 on) and publishes:

- `WinVelo-v<version>-portable.zip`
- `WinVelo-v<version>-portable.zip.sha256`, one line in `sha256sum` format: `<hash>  <file name>`
- `SHA256SUMS.txt`, the hashes of the zip, `WinVelo.exe` and `winvelo-cli.exe`
- a [build provenance attestation](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations) for the zip and both exe files

```powershell
$zip      = "WinVelo-v<version>-portable.zip"
$expected = (Get-Content "$zip.sha256" -Raw).Split(' ')[0]
(Get-FileHash $zip -Algorithm SHA256).Hash -eq $expected     # True = the file is intact

gh attestation verify $zip --repo Ziadtareks/WinVelo        # proves it was built by this repo's workflow
```

On Linux or macOS: `sha256sum -c WinVelo-v<version>-portable.zip.sha256`.

## Turning on signing (maintainers)

The release workflow already contains a signing step. It is **off** until the repository variable `WINVELO_SIGNING` is set to `signpath`, so a fork or a release without a certificate never fails and never claims to be signed.

### Option A: SignPath Foundation (free for open source)

[SignPath Foundation](https://signpath.org) provides free code-signing certificates for open-source projects that meet its conditions (OSI license, public repository, builds in CI, a documented release process). WinVelo is MIT-licensed and builds in GitHub Actions.

1. Apply at <https://signpath.org> and create the project in SignPath (an artifact configuration that signs `WinVelo.exe` and `winvelo-cli.exe`, and a signing policy).
2. In GitHub, **Settings › Secrets and variables › Actions**, add:
   - secrets `SIGNPATH_API_TOKEN` and `SIGNPATH_ORGANIZATION_ID`
   - variables `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG`
3. Set the variable `WINVELO_SIGNING` to `signpath`.
4. Before the first signed release, check the [SignPath GitHub action](https://github.com/SignPath/github-action-submit-signing-request) README: its version and input names may have changed since this workflow was written.

The workflow then uploads the two exe files, waits for SignPath to sign them, checks the signatures with `Get-AuthenticodeSignature`, and only then zips the folder and writes the hashes. The hashes and the attestation therefore always describe the signed files.

### Option B: Azure Trusted Signing

[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/) is a paid Microsoft service. To use it, replace the SignPath steps with Microsoft's signing action between the `-SkipZip` and `-PackageOnly` steps of `build/publish.ps1`, and sign the same two files.

## Local builds

`.\build\publish.ps1` builds, zips and hashes in one go. To sign locally:

```powershell
.\build\publish.ps1 -SkipZip        # build dist\WinVelo-v<version>-portable\
# sign dist\WinVelo-v<version>-portable\WinVelo.exe and winvelo-cli.exe with your tool
.\build\publish.ps1 -PackageOnly    # zip and write the .sha256 and SHA256SUMS.txt files
```

A self-signed test certificate (`New-SelfSignedCertificate -Type CodeSigningCert`) is only useful for your own tests: other PCs do not trust it and SmartScreen still warns.
