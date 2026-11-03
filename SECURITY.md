# Security policy

Tobtech maintains the newest published Brickläsaren preview. Until real-reader acceptance testing is complete, releases remain previews. Security fixes are delivered in new versions; published packages are not replaced in place.

## Report a vulnerability

Do not post tag numbers, reader profiles, private keys, logs containing personal data, or exploit details in a public issue.

When this repository is public and private vulnerability reporting is enabled, use **Security → Report a vulnerability**. While it is private, contact the repository owner through your existing private contact channel. If you have no private channel, an issue may ask the owner to establish one, without including the vulnerability or sensitive details. Do not assume an ordinary issue is private after the repository becomes public.

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

CI has read-only repository permissions, no private signing key, GitHub-hosted standard runners, pinned actions, execution time limits, cancellation of superseded jobs and short artifact retention. The repository has an Actions budget of zero additional spend; runs stop if included usage is exhausted. No paid security product is required.

GitHub Free does not enforce protected branches/rulesets on this private personal repository. Native GitHub secret/code scanning and private vulnerability reporting have availability restrictions. Gitleaks and offline tests provide free checks now; enable eligible GitHub protections explicitly if the repository is later made public. The repository must not be made public merely to enable a check.

## Release review

Before publishing, review the diff and dependencies, require successful Windows, macOS and secret checks, build from the verified commit, inspect package contents, upload assets to a draft release, verify their digests, then publish. Sign the update feed only after verifying the final release assets. Use the saved signing key; never generate a replacement silently.

Known limits: signed feeds do not yet have expiry/revocation metadata or a key-rotation protocol; an attacker controlling hosting may withhold updates. Offline tests do not replace hardware, hostile-network or independent penetration testing. A detected input error must fail closed, with no fallback to an unverified reader or unsigned update.
