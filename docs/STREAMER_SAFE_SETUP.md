# Streamer Safe OBS Setup

EMX Clips v0.1.15 and newer uses a dedicated OBS workspace by default.

## What EMX Creates

- OBS profile: `EMX Clips`
- OBS scene collection: `EMX Clips`
- Source: `EMX Display Capture`
- Optional source: `EMX Mic Capture`

When EMX launches OBS, it clones the current working OBS profile and scene collection into the EMX workspace, then opens OBS directly in that workspace.

EMX applies replay buffer, recording path, canvas size, display capture, and mic settings inside that EMX workspace.

## Streamer Safety

If OBS is already open in another profile or scene collection, EMX does not force OBS to switch. It stops setup and shows a warning instead.

This prevents EMX from changing a live stream setup while someone is live or preparing to go live.

## User Guide

1. Install OBS and open it in Normal Mode once.
2. In OBS, enable `Tools > WebSocket Server Settings`.
3. Close OBS.
4. Open EMX Clips and let EMX launch OBS.
5. Leave `Streamer safe: use EMX OBS profile and scene collection` enabled.
6. Click `Save Settings`.
7. Click `Auto Setup Capture`.
8. Click `Auto Setup Mic` if mic capture is wanted.
9. Minimize EMX Clips to tray.
10. Press the clip hotkey to save clips.

If OBS is already open in a different profile, close OBS and fully exit EMX Clips from the tray. Then open EMX Clips again so it can launch OBS into the `EMX Clips` workspace.

If you intentionally want EMX Clips to use the currently open OBS profile, turn off `Streamer safe: use EMX OBS profile and scene collection`.

Streamers should keep their normal stream profile/scene collection separate. EMX will use its own `EMX Clips` workspace for clipping.
