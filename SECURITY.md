# Security policy

Tobtech maintains the newest published Brickläsaren preview. Until real-reader acceptance testing is complete, releases remain previews. Security fixes are delivered in new versions; published packages are not replaced in place.

## Report a vulnerability

Do not post tag numbers, reader profiles, private keys, logs containing personal data, or exploit details in a public issue.

Use **Security → Report a vulnerability** to send a private report to the maintainer. Ordinary issues, pull requests and their attachments are public; do not put vulnerability details or sensitive data there.

Include the app version, operating system, prerequisites, impact and a minimal reproduction using synthetic data. Test only systems and devices you own or are authorized to test.

## Trust boundaries

- The app has no inbound network service. Updates are outbound HTTPS requests to fixed GitHub hosts. Windows reader communication uses local USB networking; macOS uses the local smart-card/USB interfaces.
- Run as an ordinary user. Keep the application and its settings in locations other users cannot modify. Never weaken PowerShell, antivirus or operating-system protections to start the app.
- Update feeds use a pinned RSA public key. The signing private key must stay outside Git, builds, CI and logs, with owner-only permissions and a private backup. Compromise of the signing computer or key can compromise future updates.
- An authentic initial download is still necessary. Feed signatures are not Windows publisher signing or Apple Developer ID/notarization. GitHub release immutability protects future published release assets and tags; it does not authenticate a copy obtained from another website.
- Reader IDs, USB identifiers and private IP addresses are not cryptographic device authentication. A hostile USB device, compromised driver, local malware or administrator is outside the trust boundary.
- Copying a number puts it on the operating system clipboard. Other apps or clipboard history/synchronization may retain it; clearing the app's clipboard value cannot retract those copies.

## Free security checks

Run `python3 tools/scan-secrets.py` to scan Git history and tracked working files. It downloads a fixed, SHA-256-verified version of the MIT-licensed Gitleaks CLI to a temporary directory, runs locally, redacts findings and uploads no source or reports. On Windows, `python` can be used instead of `python3`. The official CLI requires no subscription or license key.

Opt into the local pre-commit check with `git config --local core.hooksPath .githooks`. Local hooks and CI are useful checks, but do not replace server-enforced branch protection and can be bypassed by someone with write access.

Build and test jobs have read-only repository permissions. CodeQL analysis of C#, C, Swift and Python additionally has permission to publish security findings, but cannot write source or releases. CI uses no private signing key, GitHub-hosted standard runners, pinned actions, execution time limits, cancellation of superseded jobs and short artifact retention. The repository retains an Actions budget of zero additional spend. Standard hosted runners are free for this public repository; no larger paid runners are configured. No paid security product is required.

The public repository uses GitHub Free branch protection with required Windows, macOS and secret checks, secret scanning with push protection, and private vulnerability reporting. External contributors need maintainer approval before their fork workflows run. These controls complement the local Gitleaks and offline tests. No paid security subscription is required.

## Release review

Before publishing, review the diff and dependencies, require successful Windows, macOS and secret checks, build from the verified commit, inspect package contents, upload assets to a draft release, verify their digests, then publish. Sign the update feed only after verifying the final release assets. Use the saved signing key; never generate a replacement silently.

Known limits: signed feeds do not yet have expiry/revocation metadata or a key-rotation protocol; an attacker controlling hosting may withhold updates. Offline tests do not replace hardware, hostile-network or independent penetration testing. A detected input error must fail closed, with no fallback to an unverified reader or unsigned update.
