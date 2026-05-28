# EMX Clips Companion

Phone-first PWA shell for EMX Clips downloads, install instructions, and hosted pairing into the local PC clip portal.

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

## Next Piece

The desktop EMX Clips app starts a local phone companion server that:

- Starts from a `Phone Companion` button
- Shows a QR code
- Opens this hosted Vercel PWA first
- Serves recent clips on the same Wi-Fi through the paired PC portal
- Lets phones preview/share/download MP4 clips

iPhone cannot silently save video straight to Photos from a PWA. The web-safe flow is Share > Save Video.
