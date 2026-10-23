# Galaxy Keys development
- Communicate with the user in German. Code, UI, and documentation are English.
- Work on feature/test branches. Do not push until the review build is approved.
- Never commit real secrets, user vaults, Hello enrollment files, or local data.
- Local only: no network, analytics, cloud, remote fonts, or embedded credentials.
- Keep cryptographic operations in standard platform libraries. No insecure fallback when Windows Hello fails.
- Run the core regression checks and a Windows publish for changes. Windows Hello requires a real Windows device test; do not claim this was tested on Linux.
