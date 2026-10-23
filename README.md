# Galaxy Keys

A local-only Windows desktop vault for API keys and website logins. No server, account, telemetry, cloud sync, or browser extension.

**Status: v0.1.1 preview build.** The Windows x64 executable is cross-compiled on Linux. Cryptographic and persistence checks pass; the Windows UI and real Windows Hello enrollment/unlock still require on-device verification. This is not an independently audited password manager. Start with dummy entries and confirm save, restart, unlock, backup, and recovery before storing important secrets.

## Start

1. Run `GalaxyKeys.exe` on Windows 10/11 x64. No separate .NET installation is needed.
2. Create a master passphrase of at least 14 characters. Use a long, unique passphrase and retain it somewhere safe. There is no reset service or recovery backdoor.
3. Choose **Add item**, then **API Key** or **Login**. Enter a descriptive name and secret; logins also have username/email and optional website.
4. Search by item name or username. Use **All items**, **API keys**, or **Website logins** to browse.
5. In **Settings & backup**, optionally enable Windows Hello. Windows must already have Hello configured; enrollment must complete a real cryptographic decrypt and user gesture. Unsupported devices fail closed, retaining master-password access.

The EXE is unsigned. Windows may display a publisher warning or block it through Smart App Control. This project does not change Windows security settings. A signed release is a separate distribution task.

## Where data lives

`%LOCALAPPDATA%\GalaxyKeys\`

- `vault.gkv`: encrypted vault, including names, usernames, websites, passwords, and API keys.
- `vault.gkv.bak`: previous encrypted save for crash/error recovery. Deleted or edited items may remain in this previous version.
- `vault.gkv.hello`: optional RSA-wrapped vault key and Windows Hello enrollment metadata, never a plaintext key.
- `instance.lock`: lifetime exclusive handle, preventing concurrent app instances from overwriting the vault.

Do not put this folder on a cloud-synced drive if you want strictly local storage. User-created backups follow the location you choose; choose an external drive for a local backup. The application does not initiate network connections. Windows itself controls Hello enrollment and OS behavior.

## Protection

- A random 256-bit data key encrypts the entire vault with AES-256-GCM, fresh 96-bit nonces, and 128-bit authentication tags.
- A separately salted PBKDF2-HMAC-SHA256 derivation (600,000 iterations, 256-bit salt) wraps that data key with the master password using AES-GCM. Authentication binds the encrypted payload to the versioned header.
- Atomic writes flush a temporary encrypted file, then replace the vault while retaining the previous encrypted save. Failure rolls back in-memory entry mutations.
- Windows Hello uses a non-exportable RSA key in the Microsoft Passport Key Storage Provider to unwrap the data key with RSA-OAEP-SHA256. Mandatory-authentication policy and fresh gesture are required. No app-only consent check followed by DPAPI decryption, no stored master password, no software-key fallback.
- Idle lock after 5 minutes, lock on minimize, Windows-session lock, and suspend notification. Lock clears the data-key buffer and removes references to vault entries.
- Secret display hides after 15 seconds. Clipboard clears after 20 seconds, or lock/exit, only if it still holds the value the app copied. Windows clipboard-history and cloud-upload exclusion formats are set. External clipboard software may ignore these hints.
- Failed master-password attempts have an increasing UI delay; offline protection comes from the KDF and password strength, not that delay.
- Backup exports contain the encrypted vault only, without Hello enrollment.

## Limits and recovery

Encryption protects a stolen vault file, not a compromised running Windows account. Malware, a debugger, an administrator, keyloggers, screen capture, or another program reading an unlocked process can capture secrets. .NET UI strings and serialized secret strings cannot be reliably erased from managed memory. Lock removes references and zeroes owned key/plaintext byte buffers; it cannot promise complete physical memory erasure. Enable Windows device encryption/BitLocker separately for pagefile and full-disk protection.

Hello is device/user-bound and does not provide recovery on another computer. Windows reset, PIN/biometric changes, or KSP limitations can invalidate enrollment. The master password remains the portable unlock method. Disable/re-enable Hello after restoring an older or different vault if its enrollment no longer matches. The prototype deliberately fails closed if required KSP properties or RSA-OAEP are unsupported.

To restore: close the app; preserve a copy of `vault.gkv`; copy your encrypted backup (or `vault.gkv.bak`) to `vault.gkv`; reopen and use the password that protected that particular backup. Never overwrite your only good backup. Restoring is a manual full-vault replacement, not a merge.

Changing the master password updates the current vault. Existing backups, including the immediately previous `.bak`, retain their old passwords. Windows Hello continues to wrap the same data key. Revoke Hello separately to remove that access path. Losing the master password and device key means losing access.

## Build and test

Install a supported .NET 8 SDK. The project pins runtime `8.0.32` (current .NET 8 servicing release from Microsoft's release feed when this build was prepared). Update the pin as security releases become available.

```powershell
dotnet run --project tests/GalaxyKeys.Tests
dotnet publish src/GalaxyKeys -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

The bundled runtime is extracted by .NET at launch as needed; vault data still resides only in the local app-data folder. `build.ps1` performs these steps and records a checksum. The automated tests use disposable dummy secrets only. CI runs core checks and creates an unsigned Windows review artifact; it does not publish a release.

## Windows review checklist

- Create a disposable vault, add both types, search by name and username, edit/delete, restart, and check the persisted results.
- Reject a wrong password. Confirm minimize, idle, Windows lock, and sleep require unlock again.
- Confirm reveal auto-hides and clipboard clearing does not erase unrelated copied content.
- Enable Hello; verify an actual Windows prompt at enrollment and on every unlock, including repeated unlocks. Cancel must leave the vault locked. Test PIN/biometrics and unavailable-Hello fallback to master password.
- Export an encrypted backup, change the master password, restore the backup, and verify its original password works.
- Test disk-full/access-denied save failure and close/reopen; retain the last good vault.
- Inspect network traffic and verify no application-initiated connections.

MIT licensed. Repository: https://github.com/gtastories999/Galaxy-keys. This is a preview version, not a stable release.
