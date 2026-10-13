# Tweakora Dry-Run Plan: Preset `Advanced`

- **Generated At**: 2026-10-13 12:06:01 UTC
- **Preset**: `Advanced`
- **Total Tweaks In Preset**: 17
- **Total Actions Planned**: 42

## Planned Execution Actions

### `advanced.removeonedrive`: Microsoft OneDrive - Remove (Risk: **High**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `# Deny permission to remove OneDrive folder
      icacls $Env:OneDrive /deny ...` |

### `advanced.windowsai`: Windows AI - Disable And Remove (Risk: **High**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\SettingsPageVisibility` | `Set` | `(Not set)` | `hide:aicomponents` |
| Registry | `HKLM\SOFTWARE\Policies\WindowsNotepad\DisableAIFeatures` | `Set` | `(Not set)` | `1` |
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `$Appx = (Get-AppxPackage MicrosoftWindows.Client.CoreAI).PackageFullName
    ...` |

### `advanced.rightclickmenu`: Right-Click Menu Previous Layout - Enable (Risk: **Low**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `New-Item -Path "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905b...` |

### `essential.activity`: Activity History - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableActivityFeed` | `Set` | `(Not set)` | `1` |
| Registry | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\PublishUserActivities` | `Set` | `(Not set)` | `0` |
| Registry | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\UploadUserActivities` | `Set` | `(Not set)` | `0` |

### `essential.widget`: Widgets - Remove (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `# Sometimes if you dont stop the Widgets process the removal may fail

      ...` |

### `essential.revertstartmenu`: Start Menu Previous Layout - Enable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SYSTEM\ControlSet001\Control\FeatureManagement\Overrides\8\3036241548\EnabledState` | `Set` | `(Not set)` | `1` |

### `essential.disablestoresearch`: Microsoft Store Recommended Search Results - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `icacls "$Env:LocalAppData\Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\Local...` |

### `essential.location`: Location Tracking - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location\Value` | `Set` | `(Not set)` | `Deny` |
| Registry | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}\SensorPermissionState` | `Set` | `(Not set)` | `0` |
| Registry | `HKLM\SYSTEM\Maps\AutoUpdateEnabled` | `Set` | `(Not set)` | `0` |
| Service | `lfsvc` | `SetStartup` | `(Not Installed)` | `Disabled` |

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

### `essential.deliveryoptimization`: Delivery Optimization - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode` | `Set` | `(Not set)` | `0` |

### `essential.restorepoint`: Restore Point - Create (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore\SystemRestorePointCreationFrequency` | `Set` | `(Not set)` | `0` |
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `if (-not (Get-ComputerRestorePoint)) {
          Enable-ComputerRestore -Driv...` |

### `essential.endtaskontaskbar`: End Task With Right Click - Enable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings\TaskbarEndTask` | `Set` | `(Not set)` | `1` |

### `essential.wpbt`: Windows Platform Binary Table (WPBT) - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| Registry | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\DisableWpbtExecution` | `Set` | `(Not set)` | `1` |

### `essential.deletetempfiles`: Temporary Files - Remove (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `# A temp folder always holds files something has open, including this run's o...` |

### `essential.disableexplorerautodiscovery`: File Explorer Automatic Folder Discovery - Disable (Risk: **Safe**)
| Action Type | Target | Operation | Current State | Proposed State |
|---|---|---|---|---|
| PowerShell | `System PowerShell Runtime` | `Execute` | `Unmodified` | `# Previously detected folders
      $bags = "HKCU:\Software\Classes\Local Set...` |
