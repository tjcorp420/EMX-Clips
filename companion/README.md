# EMX Clips Companion

Phone-first PWA shell for EMX Clips downloads, install instructions, QR pairing, and hosted clip viewing through Firebase Remote Share.

## Run Locally

```powershell
cd companion
npm run dev
```

Open:

```text
http://localhost:4177
```

## Deploy On Vercel

Use the `companion` folder as the Vercel project root. It is a static PWA with no build step.

## Current Status

- Installable PWA shell
- PC app download button
- iPhone Add to Home Screen guidance
- Android/desktop install prompt handling
- Hosted QR pairing with `?portal=` links from the Windows app
- Built-in camera QR scanner for pairing from the installed PWA
- EMX logo icons for iPhone home screen, desktop install, and browser tabs
- Firebase Remote Share mode with Realtime Database sessions that stay inside the Vercel app

## Next Piece

The desktop EMX Clips app starts a phone companion server that:

- Starts from a `Phone Companion` button
- Shows a QR code
- Opens this hosted Vercel PWA first
- Serves recent clips through local Wi-Fi or Firebase Remote Share plus a secure tunnel
- Lets phones preview/share/download MP4 clips

iPhone cannot silently save video straight to Photos from a PWA. The web-safe flow is Share > Save Video.
