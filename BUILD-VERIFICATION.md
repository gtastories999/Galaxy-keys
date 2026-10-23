# Review build verification

- Target: Windows 10/11 x64, portable self-contained EXE.
- Runtime: .NET 8.0.32 from the official .NET release feed.
- Build: cross-compiled on Linux, zero compiler warnings/errors.
- Core checks: 21 passed; see TEST-RESULTS.txt.
- File format: PE32+ Windows GUI executable, x86-64.
- SHA-256: `9a6fc2ee077308d6acd0381b9e0fb1504a2ad3b18b3138e6f36ea4e015d2374d`
- Not executed on Windows in this environment. UI, Windows Hello, clipboard flags, and OS session notifications still need the documented Windows device checks.
- Unsigned; no independent security audit. No real secrets used during testing.

The Linux build environment needed a temporary process-path compatibility shim outside the project. It is not included in the source or executable.
