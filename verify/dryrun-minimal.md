# Tweakora Dry-Run Plan: Preset `Minimal`

- **Generated At**: 2026-10-13 12:05:50 UTC
- **Preset**: `Minimal`
- **Total Tweaks In Preset**: 4
- **Total Actions Planned**: 21

## Planned Execution Actions

### `essential.services`: Services - Set to Manual (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Service | `CscService` | `SetStartup` | `(Not Installed)` | `Disabled` |
| Service | `DiagTrack` | `SetStartup` | `(Not Installed)` | `Disabled` |
| Service | `MapsBroker` | `SetStartup` | `(Not Installed)` | `Manual` |
| Service | `StorSvc` | `SetStartup` | `(Not Installed)` | `Manual` |
| Service | `SharedAccess` | `SetStartup` | `(Not Installed)` | `Disabled` |
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `$Memory = (Get-CimInstance Win32_PhysicalMemory | Measure-Object Capacity -Su...` |

### `essential.consumerfeatures`: ConsumerFeatures - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableWindowsConsumerFeatures` | `Set` | `(Not set)` | `1` |

### `essential.telemetry`: Telemetry - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled` | `Set` | `(Not set)` | `0` |
| Registry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy\TailoredExperiencesWithDiagnosticDataEnabled` | `Set` | `(Not set)` | `0` |
| Registry | `HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy\HasAccepted` | `Set` | `(Not set)` | `0` |
| Registry | `HKCU\Software\Microsoft\Input\TIPC\Enabled` | `Set` | `(Not set)` | `0` |
| Registry | `HKCU\Software\Microsoft\InputPersonalization\RestrictImplicitInkCollection` | `Set` | `(Not set)` | `1` |
| Registry | `HKCU\Software\Microsoft\InputPersonalization\RestrictImplicitTextCollection` | `Set` | `(Not set)` | `1` |
| Registry | `HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore\HarvestContacts` | `Set` | `(Not set)` | `0` |
| Registry | `HKCU\Software\Microsoft\Personalization\Settings\AcceptedPrivacyPolicy` | `Set` | `(Not set)` | `0` |
| Registry | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection\AllowTelemetry` | `Set` | `(Not set)` | `0` |
| Registry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Start_TrackProgs` | `Set` | `(Not set)` | `0` |
| Registry | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\PublishUserActivities` | `Set` | `(Not set)` | `0` |
| Registry | `HKCU\Software\Microsoft\Siuf\Rules\NumberOfSIUFInPeriod` | `Set` | `(Not set)` | `0` |
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `# Disable Defender Auto Sample Submission
      Set-MpPreference -SubmitSampl...` |

### `essential.wpbt`: Windows Platform Binary Table (WPBT) - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\DisableWpbtExecution` | `Set` | `(Not set)` | `1` |
