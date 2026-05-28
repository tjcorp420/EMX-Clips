# Firebase Remote Share

Firebase Remote Share keeps the phone inside the hosted Vercel companion without using Firebase Storage.

## How It Works

1. EMX Clips starts the local clip server on the PC.
2. EMX Clips starts a free Cloudflare quick tunnel to that local server.
3. EMX Clips writes the tunnel session to Firebase Realtime Database.
4. The QR opens `https://emx-clips-companion.vercel.app/?remoteSession=...`.
5. The Vercel app reads the session, lists clips, previews videos, downloads files, and opens the phone share sheet.

The clips are still streamed from your PC, but the phone stays on the Vercel app.

## Firebase Setup

In Firebase Console:

1. Enable Authentication > Sign-in method > Anonymous.
2. Create Realtime Database.
3. Use the Web App `apiKey`.
4. Use this database URL:

```text
https://emxclips-86f00-default-rtdb.firebaseio.com
```

EMX Clips already includes the default API key and Realtime Database URL for the `emxclips-86f00` project.

## Realtime Database Rules

Use these rules after basic test mode is working:

```json
{
  "rules": {
    "emxClipSessions": {
      "$sessionId": {
        ".read": "auth != null",
        ".write": "auth != null && (!data.exists() || data.child('owner').val() == auth.uid || newData.child('owner').val() == auth.uid)"
      }
    }
  }
}
```

For first testing, Firebase test mode is fine. After confirming it works, paste the rules above.

## Notes

- Keep EMX Clips open on the PC while the phone is viewing clips.
- The tunnel URL changes when EMX restarts Remote Share.
- iPhone still uses Share > Save Video to put videos into Photos.
- Firebase Realtime Database stores only the session info, not the video files.
