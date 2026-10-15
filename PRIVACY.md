# Privacy Policy

**Effective Date:** October 15, 2026  
**Product:** WinVelo (`WinVelo.exe`, `winvelo-cli.exe`)

WinVelo is built on a simple foundation: **your computer belongs to you, and your configuration is your personal business.**

---

## 1. Zero Telemetry & Zero Tracking Commitment

- **No Analytics**: WinVelo contains no telemetry libraries, tracking pixels, crash analytics beacons (like Sentry or AppCenter), or unique device identifiers.
- **No Advertising SDKs**: There are zero promotional frameworks or ad networks included in any part of WinVelo.
- **No Background Polling**: WinVelo runs no background daemons, telemetry listeners, or scheduled phone-home tasks.
- **Local-Only Journaling**: All rollback journal files, baseline records, and logs are saved exclusively to your local disk (`%APPDATA%\WinVelo` and `%LOCALAPPDATA%\WinVelo`). They are never uploaded anywhere.

---

## 2. Complete Inventory of Network Activity

WinVelo makes **zero network requests** during ordinary startup, system status inspection, tweak detection, tweak application, or rollback.

Below is an exhaustive list of every feature that can initiate an outbound network request, all of which require **direct, intentional user initiation**:

### A. Software Package Installation (Apps Tab)
- **Mechanism**: Calls the official Microsoft Windows Package Manager (`winget.exe`) or Chocolatey (`choco.exe`).
- **Destinations**: Microsoft Package Manager CDN (`winget.azureedge.net`, `cdn.winget.microsoft.com`) and vendor software repositories for packages the user chooses to install.
- **Trigger**: Occurs **only** when the user selects packages and clicks "Install".

### B. DNS Benchmark & Speed Measurement (Tools Tab)
- **Mechanism**: Sends standard ICMP echo requests (pings) or basic DNS resolution probes to public DNS servers to measure latency.
- **Destinations**: Public resolver IP addresses selected by the user (Cloudflare `1.1.1.1`, Google `8.8.8.8`, Quad9 `9.9.9.9`, AdGuard `94.140.14.14`).
- **Trigger**: Occurs **only** when the user navigates to the DNS tool and clicks "Run Benchmark".

### C. Manual Release Checks
- **Mechanism**: Direct HTTPS request to the public GitHub API.
- **Destination**: `https://api.github.com/repos/winvelo/winvelo/releases/latest`.
- **Data Sent**: Standard anonymous HTTP user-agent header. No device identifiers, IP logs, or tweak states are transmitted.
- **Trigger**: Occurs **only** when the user clicks "Check for Updates" or clicks the About dialog release link. There is **no automatic periodic polling**.

---

## 3. Diagnostic Bundles (`--diagnostics`)

When you run `winvelo-cli --diagnostics <out.zip>` to prepare a diagnostic archive for bug reporting, WinVelo:
- Gathers system OS build, tweak states, and recent log files.
- **Automatically sanitizes personal data**: Strips Windows user accounts, computer hostnames, and user directory paths (e.g. `C:\Users\YourName` is rewritten to `C:\Users\<USER>`).
- Saves the `.zip` archive **locally on your machine**. It is **never automatically uploaded**. You have full discretion to inspect the archive before deciding whether to share it with developers.
