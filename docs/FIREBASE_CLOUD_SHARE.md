# Firebase Cloud Share

Firebase Cloud Share keeps the phone on the hosted Vercel companion instead of opening the local PC Wi-Fi portal.

## What You Need From Firebase

In Firebase Console:

1. Create or open a project.
2. Add a Web app.
3. Copy these values from the web config:
   - `apiKey`
   - `storageBucket`
4. Enable Authentication > Sign-in method > Anonymous.
5. Enable Storage.

Paste `apiKey` and `storageBucket` into EMX Clips Settings, then turn on `Use Firebase Cloud Share instead of local Wi-Fi portal`.

## Storage Rules

Use these rules for the first free/test version:

```text
rules_version = '2';

service firebase.storage {
  match /b/{bucket}/o {
    match /emx-clips/{userId}/{sessionId}/{allPaths=**} {
      allow read: if true;
      allow write: if request.auth != null
        && request.auth.uid == userId
        && request.resource.size < 500 * 1024 * 1024;
    }
  }
}
```

The QR link contains a random session path, so friends can view the clips only when they have the EMX link. Writes are restricted to the anonymous Firebase user created by the PC app.

## How It Works

1. EMX Clips signs into Firebase anonymously.
2. EMX Clips uploads the latest MP4 clips and an `index.json` file to Storage.
3. The QR opens `https://emx-clips-companion.vercel.app/?cloudIndex=...`.
4. The Vercel app reads `index.json`, previews clips, downloads clips, and opens the phone share sheet.

## Notes

- MP4, M4V, and MOV are uploaded. MKV clips should be exported to MP4 first.
- Big clips use Firebase Storage bandwidth. Keep an eye on Firebase usage if you share with lots of people.
- iPhone still requires the Share sheet > Save Video flow to save into Photos.
