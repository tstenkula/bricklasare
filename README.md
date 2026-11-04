# Brickläsaren

A Tobtech desktop utility for reading access-tag numbers with an OMNIKEY 5427 CK. Windows uses PowerShell 5.1 and Windows Forms, without an application EXE. macOS uses a native Apple Silicon app (macOS 13 or later), supplied as an app and DMG.

The interface is Swedish. Supported output formats are **RCO – MIFARE + EM (mR + eR)**, RCO short/long ID, Axema VAKA and Bewator 2010/Omnis. Manufacturer and product names identify compatibility only; this project is not affiliated with those manufacturers.

## License

**Source available, not open source.** Private personal noncommercial use of official releases is free. Commercial, professional and organizational use requires a separate paid written license from Tobtech. Redistribution, rebranding and reuse in other products are restricted. Inspection, issues and proposed fixes are welcome under [LICENSE.txt](LICENSE.txt) and [CONTRIBUTING.md](CONTRIBUTING.md). No purchase or price is implied by downloading a release.

## Release status

[Official downloads](https://github.com/tstenkula/bricklasare/releases) are public and require no GitHub account. The current release is **3.6.0 preview**. Automated checks pass, but first-use setup, physical tag reads and recovery still require acceptance testing with a real reader. The stable update channel is intentionally empty until that testing is complete.

## Start

- Windows: extract the complete ZIP and open `Starta-Bricklasaren.cmd`. Choose the access system on first use. If needed, the connection guide opens the [official EEM driver page](https://www.hidglobal.com/drivers/34881). Driver binaries are not bundled.
- Mac: open the DMG and drag `Bricklasaren.app` to a writable Applications folder. The app uses native smart-card support; no Windows driver is needed. See [Mac/README.md](Mac/README.md).
- Use one reader and one tag at a time. Follow the place/remove prompts. Verify an initial result against your intended access system.

Standard formats work without a reference tag. A previously verified local override is retained and must match the exact reader and firmware. See [first-start guidance](IT/Forsta-start.md) for the distinction between a tag's radio standard and the number format chosen by an access system.

## Features

- Persistent access-system selection and optional local color/logo settings. The default is **Marinblå**, `#003B55`, with no organization branding.
- Optional automatic clipboard copy after a completed scan and verified reader restoration; optional place/remove/completion sounds. No tag register or scan history.
- Ctrl+R / ⌘R reads, Escape cancels, Ctrl/⌘+1/2 copies the first/second output and Ctrl/⌘+3 copies both outputs in MIFARE + EM mode.
- **Rensa** clears the visible result without starting another read. It only clears the clipboard if it still contains the app's own last copied text.
- Guided connection diagnostics and a report that excludes tag numbers, private profiles and secrets.
- USB changes and wake events invalidate old results and schedule a fresh connection check after ongoing recovery has finished.
- Signed update feeds with stable and preview channels. Checking never installs silently. See [Updates/README.md](Updates/README.md).

Local settings, recovery journals, reader profiles, logos and logs stay outside the source and release packages. The Windows data path remains `%LOCALAPPDATA%\Tobtech\Omnikey-RCO-v2` for compatibility; Mac uses `~/Library/Application Support/Tobtech/Bricklasaren`.

## Build and verification

Windows offline checks: `IT\Kor-sjalvtester.cmd`. Build a ZIP: `tools\build-windows.ps1`.
Mac: `Mac/build.sh`, followed by `dist/Bricklasaren.app/Contents/MacOS/Bricklasaren --self-test`. `--render-tests <folder>` renders the UI with synthetic data.

CI compiles both platforms, runs offline reader simulations, update verification and settings/resource tests, and builds packages. Hardware behavior and a full-day USB/memory soak still require a real reader. macOS builds are ad-hoc signed for Apple Silicon; they are not Developer ID signed or notarized. Windows publisher warnings are not removed by this release. Update-feed signatures authenticate downloads separately from operating-system code signing.

Version 3.5.2 also verifies signed update metadata at installation, binds Windows reader traffic to the identified USB network adapter, and checks malformed native responses with memory sanitizers. Free local/CI secret scanning, release controls and remaining trust limits are documented in [SECURITY.md](SECURITY.md).

Version 3.6.0 refines the reader workspace and result states, restores predictable keyboard focus, and avoids running the regression-test suite during normal Windows startup. Large custom logos use smaller display copies while the saved original is retained.

`SHA256SUMS.txt` covers repository files. Update feeds cover release archives. The release signing private key is never stored in this repository or in an application.
