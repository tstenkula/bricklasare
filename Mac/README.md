# Brickläsaren för macOS

Native Swift/AppKit version of Brickläsaren 3.5.1, for **Apple Silicon (arm64), macOS 13 or later**. It implements the Windows operator workflow and number formats with a native USB/PC/SC transport. There is no PowerShell, .NET, browser, background service, Python installation, Intel binary or Windows EEM driver at runtime.

## Install and run

Open `Bricklasaren-3.5.1-macOS-AppleSilicon.dmg`, drag `Bricklasaren.app` to **Program / Applications**, and open it. Connect one OMNIKEY 5427 CK or Gen2 without a tag on it. Approve the USB accessory if macOS requests it. Select the access system on first launch; no system is preselected. The choice and RCO chip selection persist per user.

The app uses an ad-hoc signature for execution on Apple Silicon. It is not Developer ID signed or notarized. A downloaded copy may require **System Settings → Privacy & Security → Open Anyway** after the first opening attempt. Do not disable Gatekeeper globally. See [Apple's instructions](https://support.apple.com/en-us/102445).

## Feature mapping

| Feature | Mac behavior |
| --- | --- |
| RCO MIFARE + EM | Same physical tag in two steps; mR/eR, both 9 digits |
| RCO | MIFARE or EM; short 9-digit and long 10-digit ID |
| Axema VAKA / Bewator 2010–Omnis | 14-digit / 13-digit EM output |
| First launch | System selection, USB discovery, automatic CCID preparation |
| Fresh reader | Built-in standard number conventions; optional verified override |
| Appearance | Marinblå `#003B55` by default, presets, custom hex, organization/subtitle, local PNG logo, preview/reset/cancel |
| Reference check | Optional IT verification, independent reference inputs, profile tied to reader ID and firmware |
| Profile import/export | Compatible PascalCase JSON with Windows v3.x; exact reader/firmware validation |
| Export ready copy | `.app`, instructions, optional verified profile; import profile in IT settings on the other Mac |
| Recovery | Journal before RF changes, restoration/readback on success/failure/cancel, interrupted-scan recovery |
| Keyboard protection | Mac uses CCID, which has no keyboard output; Windows uses HID Custom Report |
| Original USB mode | Preserved locally; explicit restore-and-quit action |
| Copy / diagnostics | Native clipboard shortcuts; bounded diagnostics without card data; report export |
| Connection settings | Native USB discovery; Windows-specific IP/EEM driver controls are unnecessary |

No automatic updates, telemetry or tag history. The application cannot guarantee compatibility with every tag solely from its radio standard: it reads an accessible UID / 40-bit EM value, not encrypted application data. RCO uses the same default 4-byte convention as Windows; customized access-system formats and randomized UIDs require separate verification. Existing corrupt or firmware-mismatched local overrides block reading instead of silently selecting another format.

## USB implementation and boundaries

The bridge in `Sources/NativeReader.c` owns PC/SC contexts and handles and releases IOKit/CF objects. All device operations execute on one serial worker queue. UI updates run on the main thread. Only one supported physical USB device may be attached. USB matching uses VID `076B`, PIDs `5427`/`5428`; board type must subsequently be `06` or `0A`, with a 16-byte reader ID and 4-byte firmware value.

Connection uses `SCARD_SHARE_DIRECT` and the CCID escape feature reported by `GET_FEATURE_REQUEST`. If no usable negotiated channel exists, read-only discovery tries the documented escape control and libccid's escape control. A mutating request is never retried across different channels. MIB commands, ATR presence parsing, card APDUs, polling settings and number conversions follow the Windows implementation. Card presence uses the reader's ATR MIB, including LF; it does not rely on the operating system exposing an EM tag as a smart card.

For a keyboard-mode reader, the documented feature report `A5 5A` requests CCID and USB re-enumeration. A journal records the original USB mode before sending it. The app then requires the same physical USB identity, verifies the reader identity, firmware and keyboard-disabled MIB value, and permits scanning only after all checks pass. The same feature-report attempt is used for an existing Custom Report interface; whether a particular firmware accepts it must be established on real hardware. A failure leaves the app unable to scan and retains the journal. It does not install drivers or weaken macOS security settings.

To restore a former keyboard/custom mode, the app preserves the original Custom Report selection, uses the documented atomic switch/apply/reboot APDU `FF6800020102`, and verifies the resulting USB enumeration. Reconnecting explicitly prepares CCID again and resolves a prior transition only after identity and CCID checks. Cancel is cooperative between device calls. A native driver call cannot be forcibly interrupted; closing is blocked while recovery is in progress. Power loss or forced termination leaves the journal for the next launch.

**Physical USB transitions, PC/SC escape support on actual Mac/reader firmware combinations, LF activation and actual tag readings still require a hardware trial.** Offline tests and rendered UI checks do not establish those facts. If a firmware refuses the documented preparation or escape path, the app reports the error and stops; further transport adaptation can follow the hardware trial.

References:

- [HID OMNIKEY 5427 CK](https://www.hidglobal.com/products/omnikey-5427ck): native CCID support for Mac.
- HID *OMNIKEY 5x27CK Software Developer Guide*, 5127-903 B.7, April 2022, §§3.2, 4.6, A.17, B.2.13. [Searchable copy of the HID-authored manual](https://www.scribd.com/document/1054261374/Omnikey-5x27ck-Software-Developer-Guide).
- [Apple smart card deployment](https://support.apple.com/guide/deployment/depd0b888248/web).
- [libccid](https://ccid.apdu.fr/): PC/SC features and CCID escape support.

## Local files and privacy

All mutable state is under `~/Library/Application Support/Tobtech/Bricklasaren/`, never in the signed application bundle. This includes `formatval.json`, `utseende.json`, `branding/`, optional `referensbricka.json`, reader-specific profiles, RF journals bound to reader identity and firmware, USB transition/original-mode journals and bounded backups. Files are ignored by Git; exported copies contain only the application, instructions and any explicitly verified reader profile. The optional reference values entered by IT are deliberately stored locally; ordinary scans are never persisted.

A per-user lock prevents two copies of the app from operating concurrently. Settings writes are atomic. Card numbers are cleared on the next read, error, system change and exit. The app clears its own last copied text only when the clipboard still contains that exact text. Diagnostic persistence and report export contain bounded error codes and app/platform state, not raw APDUs, exception dumps, card IDs, reader IDs, user paths or reference data.

## Build and verify

Requires an Apple Silicon Mac with Xcode Command Line Tools and a macOS SDK. No package downloads are needed.

```sh
Mac/build.sh
# Compiles arm64, generates the icon, ad-hoc signs the app, creates and verifies a DMG.
dist/Bricklasaren.app/Contents/MacOS/Bricklasaren --self-test
dist/Bricklasaren.app/Contents/MacOS/Bricklasaren --render-tests /tmp/bricklasare-mac-ui
```

`Mac/build.sh --no-dmg` builds just the application. The test and render commands use temporary isolated settings and a simulated transport, never a physical reader. Constructed vectors cover Windows-compatible number/APDU formats, malformed responses, new readers, interrupted operations, recovery failures, wrong-device rejection, profile binding, appearance validation, privacy and repeated editor lifecycles. Rendered screenshots use synthetic numbers only. A real long-duration hardware test remains separate. See the [recorded verification results](VERIFIERING.md).
