# EMX Clips Companion

Phone-first PWA shell for EMX Clips downloads, install instructions, and the upcoming phone clip library.

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
- Clip library UI mockup

## Next Piece

The desktop EMX Clips app needs a local phone companion server that:

- Starts from a `Phone Companion` button
- Shows a QR code
- Serves recent clips on the same Wi-Fi
- Lets phones preview/share/download MP4 clips

iPhone cannot silently save video straight to Photos from a PWA. The web-safe flow is Share > Save Video.
