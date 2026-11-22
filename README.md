# msp-tweaker

**Only the measurable.** A Windows tweaker with 13 changes, and each one has a measured effect on performance or network latency. Placebo tweaks are left out.

Each tweak shows what it does, when it helps, its trade-offs and the exact change it makes. The status of every tweak is read live from Windows. The UI has light and dark themes and is in English and Portuguese. You can switch both in the title bar, and the app remembers your choice.

## Download

1. Open the [Releases](../../releases) page and download `msp-tweaker.exe`.
2. Double-click it and accept the administrator prompt.
3. If SmartScreen says "Windows protected your PC", click **More info**, then **Run anyway**. This appears because the file is not code-signed.

You do not need to install anything, and you do not need .NET. Before you delete the app, revert the tweaks you applied.

## Tweaks

| Group | Tweak | Risk | Reboot |
| --- | --- | --- | --- |
| Power | Max Performance power plan | — | |
| GPU | Background recording off | — | |
| GPU | Optimizations for windowed games (flip model) | — | |
| GPU | Hardware-accelerated GPU scheduling | Drivers or boot | yes |
| Network | Wi-Fi without power saving | — | |
| Network | Ethernet without interrupt moderation | Drivers or boot | |
| Network | Ethernet without power saving (EEE / Green) | Drivers or boot | |
| Network | Windows Update does not saturate the connection | — | |
| Kernel & boot | Memory Integrity (HVCI) off | Kernel and security | yes |
| Kernel & boot | Hypervisor and VBS off | Kernel and security | yes |
| Kernel & boot | Spectre/Meltdown mitigations off | Kernel and security | yes |
| Kernel & boot | Message-signaled interrupts (MSI) on GPU and network | Kernel and security | yes |
| Kernel & boot | Clean boot clock "tweaks" | Drivers or boot | yes |

**Risk:** a tweak without an icon is safe and reverts instantly. A warning triangle marks tweaks that change drivers or boot settings. A lightning bolt marks tweaks that change the kernel or security settings. Before you apply a marked tweak, the app asks you to confirm and shows its trade-offs again.

## Safety

- **State is read from the live system.** The app does not rely on what it remembers. "Optimal" means there is nothing to do.
- **Every change is backed up.** Before it writes anything, the app saves the original value in `HKLM\SOFTWARE\msp-tweaker`. **Revert** restores that exact value. The backup is in HKLM so a standard user cannot plant values that Revert would then apply as admin.
- **Restore point.** Before the first marked tweak in a session, the app creates a system restore point. If Windows refuses, you can cancel or continue without one.
- **BitLocker.** If BitLocker is on, the app suspends it for one reboot before it changes boot settings, so Windows does not ask for the recovery key.
- **MSI recovery.** If a device does not start after the MSI tweak, boot into Safe Mode, open msp-tweaker and click Revert.

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

Output: `bin\Release\net10.0-windows\win-x64\publish\msp-tweaker.exe`. This is one self-contained file, and it is the file you attach to a GitHub release.

### Logo

The logo is drawn once in `Logo.xaml`. After you change it, regenerate the icon:

```powershell
powershell.exe -NoProfile -STA -File tools\make-icon.ps1
```

### Self-test

```powershell
msp-tweaker.exe --selftest
```

This checks the parsers for `powercfg`, `bcdedit` and the backup format. It does not change the system.

## License notes

The app embeds the [Barlow](https://github.com/jpt/barlow) typeface, licensed under the SIL Open Font License 1.1 (`Fonts/OFL.txt`).
