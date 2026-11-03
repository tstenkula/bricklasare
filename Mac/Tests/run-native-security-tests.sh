#!/bin/bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")/../.." && pwd)"
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/bricklasare-native-tests.XXXXXX")"
trap 'rm -rf "$build_dir"' EXIT
sdk_path="$(xcrun --sdk macosx --show-sdk-path)"
xcrun clang -target arm64-apple-macos13 -isysroot "$sdk_path" -g -O1 -Wall -Wextra -Werror \
  -fsanitize=address,undefined -fno-omit-frame-pointer \
  "$project_root/Mac/Tests/NativeSecurityTests.c" \
  -framework PCSC -framework IOKit -framework CoreFoundation \
  -o "$build_dir/native-security-tests"
ASAN_OPTIONS=abort_on_error=1 UBSAN_OPTIONS=halt_on_error=1 "$build_dir/native-security-tests"
