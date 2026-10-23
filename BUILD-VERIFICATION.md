# Galaxy Keys 0.1.1 review build verification

- Target: Windows 10/11 x64, self-contained portable EXE.
- Runtime: .NET 8.0.32.
- Build: cross-compiled on Linux, zero compiler warnings/errors.
- Core checks: 21 passed. No changes to Vault.cs or Hello.cs.
- WPF theme: compiled XAML; shared templates for rounded controls, dialogs, and window chrome.
- Assets: embedded transparent 768×768 RGBA 3D gold logo and Windows ICO with 16, 24, 32, 48, 64, 128, and 256px variants.
- SHA-256: `efe446da5a91bbd0193d65f6bf1547e90d19de16435d406c12d57ee9b7e2b383`
- Not executed on Windows in this environment. Native rendering, resizing/caption behavior, keyboard focus, dropdowns, high-DPI layouts, clipboard behavior, session notifications, and Windows Hello require on-device review.
- Unsigned; no independent security audit. No real secrets used.
