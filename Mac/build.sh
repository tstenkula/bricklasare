#!/bin/bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$project_root"
# Build outside synced folders, which can attach Finder metadata while codesign runs.
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/bricklasare-build.XXXXXX")"
trap 'rm -rf "$build_dir"' EXIT
app_dir="$build_dir/Bricklasaren.app"
mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources" "$project_root/dist"
sdk_path="$(xcrun --sdk macosx --show-sdk-path)"
xcrun clang -target arm64-apple-macos13 -isysroot "$sdk_path" -O2 -Wall -Wextra -Werror -c Mac/Sources/NativeReader.c -o "$build_dir/NativeReader.o"
xcrun swiftc -swift-version 5 -target arm64-apple-macos13 -sdk "$sdk_path" -O \
  -import-objc-header Mac/Sources/NativeReader.h Mac/Sources/*.swift Mac/Tests/*.swift \
  "$build_dir/NativeReader.o" -framework AppKit -framework PCSC -framework IOKit -framework ImageIO \
  -o "$app_dir/Contents/MacOS/Bricklasaren"
cp -X Mac/Resources/Info.plist "$app_dir/Contents/Info.plist"
cp -X LICENSE.txt "$app_dir/Contents/Resources/LICENSE.txt"
mkdir -p "$app_dir/Contents/Resources/Lucide"
cp -X Assets/Lucide/* "$app_dir/Contents/Resources/Lucide/"
cp -X Mac/Läs-mig.txt "$app_dir/Contents/Resources/Läs mig.txt"
xcrun swift Mac/Icon.swift "$build_dir/Bricklasaren.iconset"
xcrun iconutil -c icns "$build_dir/Bricklasaren.iconset" -o "$app_dir/Contents/Resources/Bricklasaren.icns"
# Ad-hoc signature is required by Apple Silicon; no certificate or identity is embedded.
codesign --force --sign - "$app_dir"
codesign --verify --deep --strict "$app_dir"
rm -rf "$project_root/dist/Bricklasaren.app"
ditto --norsrc --noextattr "$app_dir" "$project_root/dist/Bricklasaren.app"
ditto -c -k --norsrc --noextattr --keepParent "$app_dir" "$project_root/dist/Bricklasaren-3.6.2-macOS-AppleSilicon.zip"
if [ "${1:-}" = "--no-dmg" ]; then exit 0; fi
stage_dir="$build_dir/dmg"
mkdir -p "$stage_dir"
ditto --norsrc --noextattr "$app_dir" "$stage_dir/Bricklasaren.app"
cp -X Mac/Läs-mig.txt "$stage_dir/Läs mig.txt"
ln -s /Applications "$stage_dir/Program"
dmg_path="$project_root/dist/Bricklasaren-3.6.2-macOS-AppleSilicon.dmg"
hdiutil create -volname 'Brickläsaren' -srcfolder "$stage_dir" -format UDZO -ov "$dmg_path"
hdiutil verify "$dmg_path"
shasum -a 256 "$dmg_path"
