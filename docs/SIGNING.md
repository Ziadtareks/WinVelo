# Code Signing Guide for WinVelo

This document explains the code signing status of WinVelo, why Windows SmartScreen warnings appear on unsigned open-source software, and how to configure free or cloud-native code signing for automated releases.

---

## 1. Current Signing Status: Unsigned

WinVelo v1.0.0 binaries are currently **unsigned**.

Because commercial Authenticode EV certificates cost between $300 and $600 per year and require formal corporate identity verification, initial open-source releases do not bundle an expensive commercial certificate.

### Windows SmartScreen Warning
When downloading and executing an unsigned `.exe` on Windows 10/11, Windows Defender SmartScreen may display:
> *"Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting."*

To run the application:
1. Click **"More info"**.
2. Click **"Run anyway"**.

### Verifying Integrity Without a Certificate
Even without a certificate, you can cryptographically verify that the binary was not tampered with:
1. **SHA-256 Checksum Verification**:
   ```powershell
   Get-FileHash -Algorithm SHA256 .\WinVelo-v1.0.0-portable.zip
   ```
   Compare the resulting hash with `dist/SHA256SUMS.txt` or the official GitHub Release notes.
2. **GitHub Build Provenance Attestation**:
   Every release built via GitHub Actions includes an SLSA-compliant build provenance attestation (`actions/attest-build-provenance`). You can inspect the cryptographic attestation linking the binary to the exact GitHub commit and runner via the GitHub CLI:
   ```bash
   gh attestation verify WinVelo-v1.0.0-portable.zip --repo Ziadtareks/WinVelo
   ```

---

## 2. Setting Up Automated Code Signing

The release pipeline (`.github/workflows/release.yml`) is designed to check for signing secrets automatically. If secrets are present, it signs all binaries; if absent, it skips signing cleanly and publishes unsigned binaries with a transparency notice.

Two main signing paths are supported:

---

### Path A: SignPath.io (Free for Qualified Open Source Projects)

[SignPath.io](https://signpath.io) offers free Authenticode code signing for open-source projects using certificates issued by the SignPath Foundation.

#### Prerequisites:
1. Project must be open source under an OSI-approved license (WinVelo is MIT).
2. Apply for a free open-source signing account at [signpath.org](https://about.signpath.io/open-source/).
3. Create a signing project in the SignPath dashboard.

#### Required GitHub Secrets:
Add the following secrets to your GitHub repository under **Settings › Secrets and variables › Actions**:
- `SIGNPATH_API_TOKEN`: The API token generated in the SignPath management console.
- `SIGNPATH_ORGANIZATION_ID`: Your SignPath organization identifier.
- `SIGNPATH_PROJECT_SLUG`: The project slug configured in SignPath.
- `SIGNPATH_SIGNING_POLICY_SLUG`: The signing policy slug (e.g., `test-signing` or `release-signing`).

#### Workflow Integration:
When `SIGNPATH_API_TOKEN` is present, the GitHub Actions release workflow invokes the SignPath action:
```yaml
- name: Submit Signing Request to SignPath
  uses: SignPath/github-action-submit-signing-request@v1
  with:
    api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
    org-id: ${{ secrets.SIGNPATH_ORGANIZATION_ID }}
    project-slug: ${{ secrets.SIGNPATH_PROJECT_SLUG }}
    signing-policy-slug: ${{ secrets.SIGNPATH_SIGNING_POLICY_SLUG }}
    artifact-path: 'dist/unsigned'
    output-path: 'dist/signed'
```

---

### Path B: Azure Trusted Signing (Microsoft Cloud HSM)

[Azure Trusted Signing](https://learn.microsoft.com/en-us/azure/trusted-signing/) (formerly Microsoft Azure Code Signing) provides low-cost, identity-verified cloud code signing backed directly by Microsoft.

#### Prerequisites:
1. Active Microsoft Azure subscription with Trusted Signing Account.
2. Certificate Profile configured for public trust.
3. Microsoft Entra ID (Azure AD) App Registration with the `Trusted Signing Certificate Profile Signer` role.

#### Required GitHub Secrets:
- `AZURE_TENANT_ID`: Microsoft Entra tenant ID.
- `AZURE_CLIENT_ID`: App registration Application (client) ID.
- `AZURE_CLIENT_SECRET`: App registration Client secret.
- `AZURE_TRUSTED_SIGNING_ENDPOINT`: Region endpoint (e.g., `https://eus.codesigning.azure.net`).
- `AZURE_TRUSTED_SIGNING_ACCOUNT`: Your Trusted Signing account name.
- `AZURE_TRUSTED_SIGNING_PROFILE`: Your Certificate Profile name.

#### Workflow Integration:
```yaml
- name: Azure Trusted Signing
  uses: azure/trusted-signing-action@v0.4.1
  with:
    azure-tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    azure-client-id: ${{ secrets.AZURE_CLIENT_ID }}
    azure-client-secret: ${{ secrets.AZURE_CLIENT_SECRET }}
    endpoint: ${{ secrets.AZURE_TRUSTED_SIGNING_ENDPOINT }}
    trusted-signing-account-name: ${{ secrets.AZURE_TRUSTED_SIGNING_ACCOUNT }}
    certificate-profile-name: ${{ secrets.AZURE_TRUSTED_SIGNING_PROFILE }}
    files: |
      dist/**/WinVelo.exe
      dist/**/winvelo-cli.exe
```

---

## 3. Local / Self-Signed Testing (Development Only)

For local development or testing with SmartAppControl/Device Guard in audit mode:
```powershell
# Create a test self-signed certificate in CurrentUser\My
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=WinVelo-Dev-Test" -CertStoreLocation "Cert:\CurrentUser\My"

# Sign local test binary
Set-AuthenticodeSignature -Certificate $cert -FilePath .\src\Tweakora.App\bin\Release\net10.0-windows\WinVelo.exe -TimestampServer "http://timestamp.digicert.com"
```
*(Note: Self-signed certificates are not trusted by other computers and will not suppress SmartScreen).*
