# Security policy

This is a pre-audit prototype. Do not treat a successful build or automated tests as a security audit. Please report vulnerabilities privately to the repository owner once a reporting channel is configured; do not include real credentials or user vaults in issues.

Trust boundary: the vault file is protected against disclosure and modification without an accepted unlock key. It does not protect secrets from malicious code running in an unlocked desktop session or an administrator. There is no anti-rollback protection against replacing the entire vault with an older valid encrypted copy.

Windows Hello is an alternative key-unwrapping path. Its policy checks and gesture requirement fail closed; a Windows device test is required before relying on it. No private key is exported, and no master password is retained for Hello. Application identity is not an isolation boundary against arbitrary full-trust code running as the same Windows user.

Builds are unsigned and no independent audit has been performed. Keep the runtime pin serviced. Do not add network services, plaintext exports, logging of secrets, insecure credential recovery, or a DPAPI-only unlock fallback without explicitly redesigning the threat model.
