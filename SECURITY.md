# Security Policy

## Supported Versions

Only the latest release of WinVelo receives security updates and vulnerability triage.

| Version | Supported          |
| ------- | ------------------ |
| 0.9.x (beta) | :white_check_mark: |
| < 0.9.0 | :x:                |

---

## Reporting a Vulnerability

The WinVelo maintainers take security and system integrity seriously. Because WinVelo operates with Administrator elevation to configure system services, policies, and registry keys, vulnerability reports are handled with high priority.

If you believe you have discovered a security vulnerability in WinVelo (such as an elevation-of-privilege bug, path traversal, untrusted code execution, or insecure fallback), please follow these steps:

1. **Do NOT open a public GitHub issue.**
2. Send an email to **security@winvelo.org** (or use GitHub Private Vulnerability Reporting on the repository if enabled).
3. Include the following details in your report:
   - A description of the vulnerability and its potential impact.
   - Affected version(s) of WinVelo.
   - Exact, step-by-step reproduction instructions or a minimal Proof of Concept (PoC).
   - Any proposed remediations or patches.

---

## Response Commitment

- **Initial Acknowledgment**: You will receive an initial response acknowledging receipt within **48 hours**.
- **Triage & Assessment**: Maintainers will confirm whether the report is accepted as a vulnerability within **5 business days**.
- **Resolution & Disclosure**: We aim to release a patched version within **14 days** of confirmation, coordinated under mutual responsible disclosure.

---

## Security Boundaries & Design Principles

- **Elevation Boundary**: WinVelo checks for Administrator elevation before executing modifying commands against machine-wide keys (`HKLM`) or services. It does not attempt silent privilege escalation.
- **Write-Ahead Safety Journal**: All operations write initial system baselines to the journal before modifying host settings, ensuring safe rollbacks.
- **Privacy Sanitization**: Diagnostic bundles (`winvelo-cli --diagnostics`) scrub local machine names, usernames, and user profile directories to protect user privacy.
