# EMX Clips

EMX Clips is a lightweight Windows tray app for saving gameplay clips with a global hotkey.

The recommended first version uses OBS Studio's Replay Buffer as the capture and encoding engine, while EMX Clips provides the small branded app, hotkey, settings, and friend-friendly workflow. This keeps capture performance close to OBS instead of trying to build a risky custom encoder from scratch.

## Why This Route

- OBS already has low-overhead game capture and GPU encoders like NVENC, AMF, and Quick Sync.
- EMX Clips stays small because it only controls the replay buffer.
- It is free to build and use.
- Distribution is possible, but if you bundle OBS you must include OBS license notices and comply with GPL requirements.

## MVP Features

- Tray app named EMX Clips
- Clickable main EMX Clips window
- Global save hotkey, default `Ctrl+Alt+F8`
- Automatically starts OBS Replay Buffer in the background after setup
- Manual restart/pause controls for the replay buffer
- Auto-launch OBS if installed
- Settings UI for OBS path, websocket port, password, clips folder, clip length, and hotkey
- Clip library with in-app preview seeking, MP4 export, file copy, delete, open location, and open folder actions
- Optional mic setup for voice and keyboard sounds
- Phone Companion for viewing, opening, downloading, and sharing clips on a phone over the same Wi-Fi
- Check Updates button backed by GitHub Releases

## How To Use It

1. Open `EMX Clips.exe`.
2. Use `Install OBS` if OBS is not detected.
3. In OBS, enable the websocket server at `Tools > WebSocket Server Settings`.
4. In EMX Clips, put the OBS websocket password in `OBS password`.
5. Click `Auto Setup Capture`.
6. Click `Auto Setup Mic` if you want mic/keyboard sounds.
7. Set your clip length and hotkeys, then click `Save Settings`.
8. Minimize EMX Clips to tray and play.
9. Use your clip hotkey in-game to save the last replay buffer clip.
10. Use the Clips tab to preview, export MP4 for CapCut/TikTok, copy files, delete, or open saved clips.
11. Click `Phone Share` to open the phone companion link/QR for moving clips to your phone.

EMX Clips starts the replay buffer in the background after setup and watches it so it comes back on if it gets stopped. Pressing your clip hotkey saves the past clip length, like Medal. It does not record a new clip from that moment forward.

If OBS shows a Safe Mode prompt, choose **Run in Normal Mode**. Safe Mode disables WebSockets, and EMX Clips cannot control OBS without WebSockets.

## Requirements

- Windows 10 or newer
- OBS Studio 28 or newer
- .NET 8 SDK to build the app

This machine currently has the .NET runtime but not the SDK. The build script can install a local SDK under `.tools/dotnet` without changing the whole machine.

## Build

```powershell
.\scripts\build.ps1 -InstallSdk
```

For a release build:

```powershell
.\scripts\build.ps1 -InstallSdk -Publish
```

The published app will appear under the path below as a self-contained single-file Windows executable, so friends do not need to install .NET separately:

```text
src\EmxClips\bin\Release\net8.0-windows\win-x64\publish\
```

## OBS Setup

See [OBS setup](docs/OBS_SETUP.md).

If OBS shows a crash/safe-mode prompt, choose **Run in Normal Mode**. EMX Clips cannot control OBS in Safe Mode because OBS disables WebSockets there.

## Distribution Notes

See [distribution notes](docs/DISTRIBUTION.md).

## Auto Updates

See [auto-update plan](docs/AUTO_UPDATES.md). EMX Clips has a Check Updates button that reads `update-manifest.json` from GitHub Releases and can download, verify, replace, and restart the app.

## Phone Companion

See [phone companion notes](docs/PHONE_COMPANION.md). The phone companion runs from the PC app and serves clips on the local Wi-Fi network. MP4 clips are best for iPhone/Android playback.
