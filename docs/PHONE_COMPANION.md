# EMX Phone Companion

EMX Clips includes a phone companion server for moving clips from PC to phone.

It also has optional Firebase Remote Share for keeping the phone inside the Vercel companion app without Firebase Storage. See [Firebase Remote Share](FIREBASE_REMOTE_SHARE.md).

## How To Use

1. Open EMX Clips on the PC.
2. Click `Phone Share` on the Clips tab or `Phone Companion` on Settings.
3. Scan the QR code with your phone camera, or open the installed EMX Companion PWA and tap `Scan PC QR`.
4. Firebase Remote Share is the default, so the Vercel app opens the clip library inside the app.
5. In local Wi-Fi mode, tap `Open My PC Clips` and keep the PC and phone on the same Wi-Fi.
6. Preview, open, download, or share clips from the phone page.

## iPhone Photos

iOS does not let a web app silently save a video straight into Photos. Use:

1. Tap `Open Video`.
2. Tap the iOS Share button.
3. Tap `Save Video`.

## Troubleshooting

- If the phone cannot connect, allow EMX Clips through Windows Firewall for Private networks.
- MP4 files work best on phones. If a clip is MKV, use `Export MP4` in EMX Clips first.
- The Vercel companion page is the branded phone entry point and has its own QR scanner.
- Firebase Remote Share uses Realtime Database plus a secure tunnel so the phone can view clips outside local Wi-Fi while EMX Clips stays open on the PC.
- If you see a raw `http://10.x.x.x:4788/` link, you are in local fallback mode. Use v0.1.11 or newer and keep the Firebase API key plus Realtime Database URL filled in Settings.
