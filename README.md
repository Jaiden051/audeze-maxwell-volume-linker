# Audeze Maxwell Volume Linker

I wanted a way to control both streams of audio from my Audeze Maxwell (game and chat) from my keyboard's volume roller on Windows. As I lack the skills and cannot be bothered to learn I tasked AI to do it, I imagine it is not of the greatest quality and it could be better but it does work for me on Windows 10, it auto launches and automatically re links the volume after disconnecting and reconnecting. Feel free to do whatever you want and I hope someone else with this niche issue finds this and enjoys it, thank you! p.s I have no clue what gemini did with my name as the contributor

everything after this is AI generated.

A lightweight, zero-dependency Windows system tray utility that automatically links the volume and mute state between **Speakers (Game-Audeze Maxwell)** and **Speakers (Chat-Audeze Maxwell)** (or any two chosen audio output devices) in real time.

When adjusting volume on Windows with a keyboard volume wheel or media keys, Windows only adjusts the default playback endpoint (`Game`), leaving the `Chat` stream at a different volume. **Audeze Maxwell Volume Linker** solves this by synchronizing both streams instantaneously.

---

## Features

- **Instant Two-Way Volume & Mute Sync**: Adjusting the volume or mute state on either stream (via keyboard media keys/volume wheel, Windows sound mixer, or headset wheel) immediately sets the paired stream to the exact same percentage and mute state.
- **Event-Driven CoreAudio Engine (Zero Polling)**: Uses native Windows CoreAudio COM callbacks (`IMMNotificationClient` and `IAudioEndpointVolumeCallback`) with loop-prevention context GUIDs (`guidEventContext`) so there is zero feedback loop and near-zero CPU/RAM usage.
- **Automatic Disconnect & Reconnect Handling**: Automatically detects when the Audeze Maxwell headset is powered off or unplugged, cleanly releases audio handles, and re-links the streams as soon as the headset is turned back on.
- **Custom Device Picker**: Supports auto-detecting the Audeze Maxwell `Game` and `Chat` endpoints out of the box, or lets you pick **any two playback devices** on your system via the **Select Synced Devices...** dialog.
- **System Tray Control**:
  - **Green Icon**: Headset connected and streams linked.
  - **Amber Icon**: Headset connected and muted.
  - **Gray Icon**: Headset turned off / disconnected (or sync paused).
  - **Double-Click Tray Icon**: Opens the **Select Synced Audio Streams** window.
  - **Right-Click Menu**: Toggle **Link Audio Volumes**, open **Select Synced Devices...**, trigger **Reconnect / Sync Now**, toggle **Start Automatically on Login**, or **Exit**.

---

## Installation

### Option 1: Standalone Installer (Recommended)
1. Download **[`AudezeMaxwellVolumeLinker-Setup.exe`](AudezeMaxwellVolumeLinker-Setup.exe)** from the repository or the **Releases** page.
2. Run `AudezeMaxwellVolumeLinker-Setup.exe`.
3. Choose your desired options (Start automatically on Windows login, Start Menu shortcut, Desktop shortcut) and click **Install**.
   - Installs to `%LocalAppData%\AudezeMaxwellVolumeLinker` by default (no administrator elevation required).
   - Includes a full uninstaller registered in Windows **Settings → Apps → Installed apps**.

### Option 2: Portable Executable
1. Download **[`VolumeLinker.exe`](VolumeLinker.exe)** and run it from any folder.

---

## Building from Source

No external SDKs, NuGet packages, or third-party dependencies are required. The project compiles directly using the built-in Windows .NET Framework C# compiler (`csc.exe`).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

This compiles:
1. `VolumeLinker.exe` (from `VolumeLinker.cs`)
2. `AudezeMaxwellVolumeLinker-Setup.exe` (from `Setup.cs`, embedding `VolumeLinker.exe` inside)

### CLI Diagnostic Check
You can inspect the current endpoint discovery status from a terminal with:
```powershell
.\VolumeLinker.exe --status | Out-Host
```

---

## License

Released under the [MIT License](LICENSE).
