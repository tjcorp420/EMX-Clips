# OBS Setup For EMX Clips

EMX Clips controls OBS through the built-in OBS websocket server.

## Install OBS

Install OBS Studio from:

https://obsproject.com/

## Configure Replay Buffer

1. Open OBS.
2. Go to `Settings > Output`.
3. Set `Output Mode` to `Simple` for the first version.
4. Set `Recording Path` to your clips folder, for example:

   ```text
   C:\Users\<you>\Videos\EMX Clips
   ```

5. Choose a hardware encoder if available:
   - NVIDIA: NVENC
   - AMD: AMF
   - Intel: Quick Sync
6. Enable `Replay Buffer`.
7. Pick a replay length, such as `60` seconds.

## Configure Capture

For most games:

1. In OBS, create a scene named `EMX Clips`.
2. Add a `Game Capture` source.
3. Set it to capture any fullscreen application.

If that does not work for a specific game, use `Display Capture` as a fallback.

## Enable Websocket Control

1. In OBS, open `Tools > WebSocket Server Settings`.
2. Enable the websocket server.
3. Keep the port as `4455`.
4. Set a password, or leave it blank for local-only testing.
5. Put the same password in EMX Clips settings.

## Use

1. Start OBS.
2. Start EMX Clips.
3. Use `Ctrl+Alt+F8` to save the replay buffer.

EMX Clips can also auto-launch OBS if the OBS path is set or OBS is installed in the default location.

## If OBS Shows A Safe Mode Prompt

Choose `Run in Normal Mode`.

Do not choose Safe Mode for EMX Clips. OBS Safe Mode disables third-party plugins, scripting, and WebSockets. EMX Clips uses WebSockets to start the replay buffer and save clips.

This prompt usually means OBS thinks the last shutdown was not clean. It can happen after a crash, a forced Task Manager close, or shutting down Windows while OBS is still open.
