# Distribution Notes

EMX Clips itself can be distributed as your own app.

If you tell friends to install OBS separately, distribution is simple: ship EMX Clips and your setup guide.

If you bundle OBS with EMX Clips, you must follow OBS Studio's license terms. OBS Studio is GPL-licensed, which generally means you need to include the GPL license text, preserve notices, and provide corresponding source code for the GPL-covered parts you distribute. Do not imply that OBS Studio itself is your private closed-source product.

Recommended friend build for v1:

- Ship `EMX Clips.exe`
- Include setup docs
- Tell users to install OBS from the official OBS site

Recommended polished build later:

- Installer that detects OBS
- Optional portable OBS bundle with license notices
- First-run setup wizard
- Code signing certificate to avoid Windows SmartScreen warnings

