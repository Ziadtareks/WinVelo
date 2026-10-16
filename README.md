<p align="center">
  <img src="docs/branding/winvelo-256.png" width="112" alt="WinVelo logo" />
</p>

<h1 align="center">WinVelo</h1>

<p align="center">
  A transparent, open-source tweak tool for Windows 10 and 11.<br/>
  See exactly what every setting changes, apply it, and undo it.
</p>

<p align="center">
  <a href="https://github.com/Ziadtareks/WinVelo/releases/latest"><img src="https://img.shields.io/badge/release-v1.0.0-2563EB.svg" alt="Release v1.0.0"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4.svg" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/tweaks-152-10B981.svg" alt="152 tweaks">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="MIT License"></a>
</p>

![WinVelo](docs/screenshots/home.png)

---

## Download

1. Download **`WinVelo-v1.0.0-portable.zip`** from the [latest release](https://github.com/Ziadtareks/WinVelo/releases/latest).
2. Unzip it anywhere: your Desktop, a tools folder, or a USB stick.
3. Run **`WinVelo.exe`** and accept the administrator prompt.

There is nothing to install and you don't need to download .NET, because the runtime is built into the exe. Keep the `Catalog` folder next to `WinVelo.exe`, since it holds the tweak definitions.

> **Windows SmartScreen:** WinVelo isn't code-signed yet, so Windows may show *"Windows protected your PC"*. Click **More info**, then **Run anyway**. You can check the download against the SHA-256 hash on the release page.

**Requirements:** Windows 10 version 2004 (build 19041) or newer, or Windows 11, 64-bit.

---

## What WinVelo does

- **Shows every change before it happens.** Each tweak lists the exact registry values, services or scheduled tasks it touches, its risk level, its side effects and where the information comes from.
- **Lets you undo anything reversible.** Before a change is written, the original value is saved to a rollback journal (`%APPDATA%\WinVelo`). You can undo a single tweak, a whole session, or every change WinVelo has made.
- **Is honest about evidence.** Each tweak is labelled as *documented* (Microsoft documentation, with a quote), *UI-equivalent* (the same switch exists in Windows Settings, and the path is shown), *situational* (it fixes a specific problem), or *anecdotal*. Anecdotal tweaks stay hidden under **Experimental**.
- **Keeps Windows working.** Windows Update, the Microsoft Store, BITS and Windows Security are protected and are never disabled.

### What it doesn't do

- No "FPS boost", "RAM cleaner" or other placebo tweaks.
- No telemetry, analytics, ads or update checks. WinVelo only uses the network when you install an app or run the DNS benchmark. See [PRIVACY.md](PRIVACY.md).
- Nothing is applied without you choosing it and confirming it.

---

## Features

| | |
|---|---|
| **Home** | System status: administrator rights, restore point, number of WinVelo changes and servicing health. It also holds the presets. |
| **Presets** | Recommended, Privacy, AI & Copilot, Clean UI, Performance and Gaming, each with a *Standard* and a *Full* level. A preview shows every tweak before anything is applied, plus a "Not included" list that explains what was left out and why. |
| **Tweaks** | All 152 tweaks, searchable and filterable by category, risk (Safe, Low, Medium, High) and status (Applied, Partial, Not applied, Can't verify). **Simple mode** shows only Safe and Low tweaks that have strong evidence. |
| **Apps** | Install 36 popular apps in one go with `winget` (or Chocolatey). |
| **Tools** | Startup apps, Windows services, cleaner, DNS benchmark and switcher, common fixes, Windows Update settings, optional features and restore points. |
| **History** | Every session WinVelo ran, with one-click undo. |

<details>
<summary><b>More screenshots</b></summary>

**Tweaks**
![Tweaks](docs/screenshots/tweaks.png)

**Preset preview**
![Preview](docs/screenshots/preview.png)

**Tools**
![Tools](docs/screenshots/tools.png)

**Apps**
![Apps](docs/screenshots/apps.png)

**History**
![History](docs/screenshots/history.png)

**Settings**
![Settings](docs/screenshots/settings.png)

</details>

---

## Safety tips

- Create a restore point before your first changes. WinVelo offers to do this for you.
- Start with a preset's **Standard** level or with **Simple mode**.
- **High**-risk tweaks and irreversible actions (for example removing Edge or OneDrive) are never part of a preset and ask for extra confirmation.
- Some tweaks need a restart or an Explorer restart. The preview tells you which ones.
- You can try the whole interface without touching your system: `WinVelo.exe --test-mode`.

---

## Command line

`winvelo-cli.exe` ships in the same zip. It runs in **simulation mode by default**. Add `--real-mode` to change the real system.

```powershell
winvelo-cli --list                                # list all tweaks
winvelo-cli --info privacy.advertising.advertising-id   # details and exact changes
winvelo-cli --detect-all                          # read-only status of every tweak
winvelo-cli --preset privacy --dry-run            # preview a preset
winvelo-cli --tweak ui.explorer.show-file-extensions --apply --real-mode
winvelo-cli --tweak ui.explorer.show-file-extensions --undo --real-mode
winvelo-cli --diagnostics WinVelo-Diagnostics.zip # anonymised bundle for bug reports
```

Run `winvelo-cli --help` for every option.

---

## Build from source

You need Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/Ziadtareks/WinVelo.git
cd WinVelo

dotnet build Tweakora.sln -c Release
dotnet test src/Tweakora.Tests/Tweakora.Tests.csproj
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj

# run the app without touching your system
dotnet run --project src/Tweakora.App/Tweakora.App.csproj -- --test-mode

# build the portable package into dist\
.\build\publish.ps1
```

The source folders still use the project's former name, *Tweakora*.

### Adding a tweak

Tweaks are plain JSON files in [`Catalog/Tweaks`](Catalog/Tweaks), checked against [`Catalog/schema/tweak.schema.json`](Catalog/schema/tweak.schema.json). See the [tweak authoring guide](docs/TWEAK_GUIDE.md), then run the catalog validator and `tools/PresetGenerator` before you open a pull request.

---

## Credits

- [WinUtil](https://github.com/ChrisTitusTech/winutil) by Chris Titus Tech (MIT). Many tweaks started from WinUtil's catalog.
- [O&O ShutUp10++](https://www.oo-software.com/en/shutup10), which inspired the calm, transparent presentation.
- [WPF UI](https://github.com/lepoco/wpfui) for the Fluent controls.
- Full list: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

[MIT](LICENSE). See also [PRIVACY.md](PRIVACY.md), [SECURITY.md](SECURITY.md), [CONTRIBUTING.md](CONTRIBUTING.md) and the [CHANGELOG](CHANGELOG.md).

WinVelo changes system settings. Use it at your own risk and keep a restore point.
