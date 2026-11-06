#!/bin/bash
set -euo pipefail
lab_dir="$(cd "$(dirname "$0")" && pwd)"
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/bricklasare-firmware-lab.XXXXXX")"
trap 'rm -rf "$build_dir"' EXIT
xcrun swiftc -swift-version 5 -warnings-as-errors "$lab_dir/Firmware.swift" "$lab_dir/Tests.swift" -o "$build_dir/tests"
"$build_dir/tests"
