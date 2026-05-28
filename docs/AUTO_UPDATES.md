# Auto-Update Plan

The smart setup is a GitHub repo plus GitHub Releases.

Do not make users pull from git. Users should use the in-app Check Updates button or download the release exe.

## Recommended Structure

- GitHub repo: source code, docs, build scripts, release workflow
- GitHub Releases: finished `EMX Clips.exe` files
- Update manifest: tiny JSON file attached to each release that tells the app the newest version and download URL

## Release Flow

1. Update the version in `src/EmxClips/EmxClips.csproj`.
2. Commit changes.
3. Push to GitHub.
4. Create a tag like:

   ```powershell
   git tag v0.1.1
   git push origin v0.1.1
   ```

5. GitHub Actions builds the app and attaches `EMX.Clips.exe` plus `update-manifest.json` to a GitHub Release.

## Update Manifest

The app defaults to this stable GitHub Releases URL:

```text
https://github.com/tjcorp420/EMX-Clips/releases/latest/download/update-manifest.json
```

If the repo name changes, update `UpdateManifestUrl` in `src/EmxClips/AppSettings.cs`.

Example:

```json
{
  "version": "0.1.1",
  "downloadUrl": "https://github.com/tjcorp420/EMX-Clips/releases/download/v0.1.1/EMX.Clips.exe",
  "releaseNotesUrl": "https://github.com/tjcorp420/EMX-Clips/releases/tag/v0.1.1",
  "sha256": "PUT_RELEASE_EXE_SHA256_HERE"
}
```

## Security Notes

The updater verifies the downloaded exe when the manifest includes a real SHA-256 value. Later, code-sign the exe so Windows shows your name instead of an unknown publisher warning.

Current update UX:

- App checks whether an update exists.
- If a newer version exists, the user approves download/install.
- App downloads the new exe, verifies SHA-256 when provided, replaces itself after closing, and restarts.
