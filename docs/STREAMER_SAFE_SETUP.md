# Streamer Safe OBS Setup

EMX Clips v0.1.14 and newer uses a dedicated OBS workspace by default.

## What EMX Creates

- OBS profile: `EMX Clips`
- OBS scene collection: `EMX Clips`
- Source: `EMX Display Capture`
- Optional source: `EMX Mic Capture`

EMX applies replay buffer, recording path, canvas size, display capture, and mic settings inside that EMX workspace.

## Streamer Safety

If OBS is actively streaming or recording, EMX does not switch profiles or scene collections. It stops setup and shows a warning instead.

This prevents EMX from changing a live stream setup while someone is live.

## User Guide

1. Install OBS and open it in Normal Mode.
2. In OBS, enable `Tools > WebSocket Server Settings`.
3. Open EMX Clips.
4. Leave `Streamer safe: use EMX OBS profile and scene collection` enabled.
5. Click `Save Settings`.
6. Click `Auto Setup Capture`.
7. Click `Auto Setup Mic` if mic capture is wanted.
8. Minimize EMX Clips to tray.
9. Press the clip hotkey to save clips.

Streamers should keep their normal stream profile/scene collection separate. EMX will use its own `EMX Clips` workspace for clipping.
