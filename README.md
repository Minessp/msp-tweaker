# msp-tweaker

**Only the measurable.** A Windows tweaker that looks for measured problems (driver defaults that cut clocks, Wi-Fi scans that spike ping, CPU parking that fails, a game's frame queue) and fixes only what is actually worse than it should be. Popular placebo tweaks are left out on purpose.

Each tweak shows what it does, when it helps, its trade-offs and the exact change it makes. The status of every tweak is read live from Windows. The UI has light and dark themes and is in English and Portuguese. You can switch both in the title bar, and the app remembers your choice.

## Download

1. Download [`msp-tweaker.exe`](https://github.com/Minessp/msp-tweaker/releases/latest/download/msp-tweaker.exe) from the latest release.
2. Double-click it and accept the administrator prompt.
3. If SmartScreen says "Windows protected your PC", click **More info**, then **Run anyway**. This appears because the file is not code-signed.

You do not need to install anything, and you do not need .NET. Before you delete the app, revert the tweaks you applied.

## Tweaks

The app has two tabs. **System** holds Windows and driver tweaks; **Games** holds per-game settings (Fortnite for now) and a connection test. A tweak only shows as available when it applies to your hardware.

| Tab | Group | Tweak | Evidence | Risk | Reboot |
| --- | --- | --- | --- | --- | --- |
| System | NVIDIA driver | CUDA without dropping the memory clock (`CUDA_FORCE_P2` off) | measured clock drop (200 to 500+ MHz) | Driver | |
| System | NVIDIA driver | 10 GB shader cache | documented mechanism, not measured | — | |
| System | NVIDIA driver | Low latency for DX11 games without Reflex | measured, GPU-bound only | — | |
| System | Processor | Global timer resolution (Windows 11) | measured Sleep precision | Kernel | yes |
| System | Processor | Ryzen X3D: games on the V-Cache cores (dual-CCD parts) | measured 7 to 17% fps when it fails | — | |
| System | Network | Wi-Fi without background scans | consistent forum captures | — | |
| System | Network | Intel I225/I226: repair RSS | documented driver defect | Driver | yes |
| Games | Fortnite | Performance rendering mode | documented by Epic | — | |
| Games | Fortnite | NVIDIA Reflex On + Boost | vendor-measured | — | |
| Games | Fortnite | Frame rate cap for G-Sync/FreeSync (100 Hz+ monitors) | measured in other games | — | |

NVIDIA settings are written to the global driver profile through NVAPI, the same store the NVIDIA Control Panel uses. Fortnite settings edit `%LOCALAPPDATA%\FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini`; close the game first, because it rewrites that file when it exits. The Wi-Fi tweak installs a logon task that runs `msp-tweaker.exe --wifi-guard`, because Windows turns background scanning back on after every reconnect.

**Risk:** no icon means safe and instantly reversible. A triangle marks driver settings, a lightning bolt marks kernel or security changes. Marked tweaks ask for confirmation and create a restore point first.

### Left out on purpose

These are common in other tweakers but have no measured gain, or were measured as worse:

- **`NetworkThrottlingIndex` off:** measured higher network-driver DPC latency.
- **Memory compression off:** about 35x more paging writes, no frame-time gain.
- **Nagle off (`TcpAckFrequency`, `TCPNoDelay`), BBR2, autotuning off, RWIN, DNS for ping:** TCP-only or no effect on game traffic.
- **MMCSS `Tasks\Games`, `Win32PrioritySeparation`, process priority and affinity:** no measurement.
- **Disabling idle states, interrupt affinity, xHCI interrupt moderation:** need per-machine measurement; the last one needs a vulnerable kernel driver.
- **NVIDIA "Prefer maximum performance", forced Resizable BAR:** no measurement, or mixed results per game.
- **Fortnite `Engine.ini` cvars and "fps" launch flags:** ignored by shipping builds or unmeasured; `-NOTEXTURESTREAMING` hurts 3 to 4 GB cards.
- **Earlier versions' tweaks** (power plan, Game DVR, flip model, HAGS, VBS/HVCI, Spectre, EEE, interrupt moderation, MSI and others) were removed as obvious or unmeasured. If you applied one, it still shows up so you can revert it.

## Safety

- **State is read from the live system.** The app does not rely on what it remembers. "Optimal" means there is nothing to do.
- **Every change is backed up.** Before it writes anything, the app saves the original value in `HKLM\SOFTWARE\msp-tweaker`. **Revert** restores that exact value. The backup is in HKLM so a standard user cannot plant values that Revert would then apply as admin.
- **Restore point.** Before the first marked tweak in a session, the app creates a system restore point. If Windows refuses, you can cancel or continue without one.
- **BitLocker.** If BitLocker is on, the app suspends it for one reboot before it changes boot settings, so Windows does not ask for the recovery key.

## Requirements

- Windows 10 or 11, x64
- Administrator rights. The app always runs elevated.
- To build: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Build from source

```powershell
git clone <repo-url> msp-tweaker
cd msp-tweaker
dotnet build
.\bin\Debug\net10.0-windows\win-x64\msp-tweaker.exe
```

Windows shows a UAC prompt. If you use `dotnet run`, run it from an **administrator** terminal, because a normal terminal cannot start an elevated app.

### Single executable

```powershell
dotnet publish -c Release
```

Output: `bin\Release\net10.0-windows\win-x64\publish\msp-tweaker.exe`. This is one self-contained file.

### Releases

Every push to `main` that changes more than Markdown files runs `.github/workflows/release.yml`. It builds the exe on GitHub and publishes it as release `v1.0.<run number>`, with notes generated from the commits.

### Logo

The logo is drawn once in `Logo.xaml`; `Assets\msp-tweaker.ico` is a pre-rendered copy of it.

### Self-test

```powershell
msp-tweaker.exe --selftest
```

This checks the parsers for `powercfg`, `bcdedit` and the backup format. It does not change the system.

## License notes

The app embeds the [Barlow](https://github.com/jpt/barlow) typeface, licensed under the SIL Open Font License 1.1 (`Fonts/OFL.txt`).
