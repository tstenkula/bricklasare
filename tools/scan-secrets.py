#!/usr/bin/env python3
"""Run the free Gitleaks CLI locally. No source or findings leave this computer."""
import argparse
import hashlib
import io
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
import zipfile

VERSION = "8.30.1"
PACKAGES = {
    ("Darwin", "arm64"): ("darwin_arm64.tar.gz", "b40ab0ae55c505963e365f271a8d3846efbc170aa17f2607f13df610a9aeb6a5"),
    ("Darwin", "x86_64"): ("darwin_x64.tar.gz", "dfe101a4db2255fc85120ac7f3d25e4342c3c20cf749f2c20a18081af1952709"),
    ("Linux", "x86_64"): ("linux_x64.tar.gz", "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb"),
    ("Linux", "aarch64"): ("linux_arm64.tar.gz", "e4a487ee7ccd7d3a7f7ec08657610aa3606637dab924210b3aee62570fb4b080"),
    ("Windows", "AMD64"): ("windows_x64.zip", "d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e"),
    ("Windows", "ARM64"): ("windows_arm64.zip", "b95f5e4f5c425cedca7ee203d9afd29597e692c4924a12ed42f970537c72cc0f"),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--staged", action="store_true", help="Check only staged changes (pre-commit).")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    key = (platform.system(), platform.machine())
    if key not in PACKAGES:
        parser.error("Unsupported scanner platform; use macOS, Linux or Windows x64/arm64.")
    package, expected = PACKAGES[key]
    # Always verify the downloaded archive. Nothing is installed system-wide.
    url = "https://github.com/gitleaks/gitleaks/releases/download/v" + VERSION + "/gitleaks_" + VERSION + "_" + package
    with tempfile.TemporaryDirectory(prefix="bricklasare-secret-scan-") as directory:
        folder = Path(directory)
        with urllib.request.urlopen(url, timeout=30) as response:
            archive = response.read(32 * 1024 * 1024 + 1)
        if len(archive) > 32 * 1024 * 1024 or hashlib.sha256(archive).hexdigest() != expected:
            raise RuntimeError("Scanner download failed integrity verification; nothing was executed.")
        name = "gitleaks.exe" if key[0] == "Windows" else "gitleaks"
        if package.endswith(".zip"):
            with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
                binary = bundle.read(name)
        else:
            with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as bundle:
                member = bundle.getmember(name)
                if not member.isfile() or member.size > 100 * 1024 * 1024:
                    raise RuntimeError("Unexpected scanner archive member.")
                binary = bundle.extractfile(member).read()
        executable = folder / name
        executable.write_bytes(binary)
        executable.chmod(0o700)
        env = dict(os.environ)
        # Do not inherit an unrelated rule configuration or allow inline suppressions.
        env.pop("GITLEAKS_CONFIG", None)
        env.pop("GITLEAKS_CONFIG_TOML", None)
        flags = ["--redact=100", "--no-banner", "--no-color", "--ignore-gitleaks-allow", "--timeout", "120"]
        command = [str(executable), "git", *flags]
        command += ["--pre-commit", "--staged"] if args.staged else ["--log-opts=--all"]
        subprocess.run(command + [str(root)], cwd=root, env=env, check=True)
        if not args.staged:
            # Include tracked working changes without scanning personal ignored files.
            snapshot = folder / "source"
            snapshot.mkdir()
            paths = subprocess.check_output(["git", "ls-files", "-z"], cwd=root).split(b"\0")
            for raw in paths:
                if not raw:
                    continue
                relative = Path(os.fsdecode(raw))
                source = root / relative
                if relative.is_absolute() or ".." in relative.parts or source.is_symlink():
                    raise RuntimeError("Unsafe tracked source path.")
                if source.is_file():
                    target = snapshot / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(source, target)
            subprocess.run([str(executable), "dir", *flags, str(snapshot)], env=env, check=True)
    print("Secret scan passed. Findings are always redacted; no paid service is used.")


if __name__ == "__main__":
    main()
