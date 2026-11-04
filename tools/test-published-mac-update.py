#!/usr/bin/env python3
"""Exercise the published 3.5.2 Mac installer against public, signed 3.6.0.

Destructive installation is confined to a disposable GitHub-hosted Mac runner.
This deliberately cannot run on a developer's Mac or a self-hosted runner.
"""
import base64
import ctypes
import fcntl
import hashlib
import json
import os
import pathlib
import platform
import pwd
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request
import uuid
import xml.etree.ElementTree as ET

REPO = 'tstenkula/bricklasare'
SOURCE = '3.5.2'
TARGET = '3.6.0'
FEED_COMMIT = '136898a6df968a3e0a99b6333dbe8983e4d464c3'
FEED_SHA256 = 'cc104420dc4e9ffdbd2ca2a7d9f5563beb4efda4616c9c2fab899e355b4313d3'
SOURCE_SHA256 = '7714bbc5bc869a2835aa3ee6ce719077da6b3fbcdf0e7fc15be7b7611bfd3e6f'
HOSTS = {'github.com', 'api.github.com', 'raw.githubusercontent.com',
         'release-assets.githubusercontent.com', 'objects.githubusercontent.com'}


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def command(args, timeout=30):
    result = subprocess.run([str(a) for a in args], stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=timeout)
    require(result.returncode == 0, 'Required local verification failed: ' + pathlib.Path(str(args[0])).name)
    return result.stdout


def guard_runner():
    require(sys.platform == 'darwin' and platform.machine() == 'arm64', 'Apple Silicon macOS is required.')
    event_name = os.environ.get('GITHUB_EVENT_NAME')
    trusted_event = event_name == 'workflow_dispatch'
    if event_name == 'pull_request':
        event = json.loads(pathlib.Path(os.environ['GITHUB_EVENT_PATH']).read_text())
        pull = event.get('pull_request', {})
        trusted_event = (pull.get('head', {}).get('repo', {}).get('full_name') == REPO
                         and pull.get('base', {}).get('repo', {}).get('full_name') == REPO
                         and isinstance(pull.get('head', {}).get('ref'), str)
                         and bool(pull['head']['ref'].strip()))
    require(os.environ.get('GITHUB_ACTIONS') == 'true'
            and os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
            and os.environ.get('RUNNER_OS') == 'macOS'
            and trusted_event
            and os.environ.get('GITHUB_REPOSITORY') == REPO,
            'Refusing installation outside the authorized workflow on a disposable GitHub-hosted runner.')
    require(not os.environ.get('CFFIXED_USER_HOME')
            and pathlib.Path.home() == pathlib.Path(pwd.getpwuid(os.getuid()).pw_dir),
            'Unexpected user-directory override.')
    temporary = pathlib.Path(os.environ['RUNNER_TEMP']).resolve(strict=True)
    require(temporary.is_dir() and temporary != pathlib.Path('/'), 'Invalid runner temporary directory.')
    data_parent = pathlib.Path.home() / 'Library/Application Support/Tobtech'
    require(not data_parent.exists() and not data_parent.is_symlink(), 'Refusing to touch existing Tobtech settings.')
    usb = command(['/usr/sbin/ioreg', '-r', '-c', 'IOUSBHostDevice', '-l'])
    require(not re.search(rb'"idVendor"\s*=\s*(?:1899|0x076[bB])\b', usb), 'A physical OMNIKEY must not be connected.')
    return temporary, data_parent


def check_url(url):
    parts = urllib.parse.urlsplit(url)
    require(parts.scheme == 'https' and parts.hostname in HOSTS and parts.port in (None, 443)
            and not parts.username and not parts.password, 'Unexpected download destination.')


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        check_url(new_url)
        return super().redirect_request(request, fp, code, message, headers, new_url)


def download(url, maximum):
    check_url(url)
    request = urllib.request.Request(url, headers={'User-Agent': 'Bricklasare-published-update-test'})
    with urllib.request.build_opener(SafeRedirect()).open(request, timeout=30) as response:
        require(response.status == 200, 'Anonymous download was not successful.')
        data = response.read(maximum + 1)
    require(0 < len(data) <= maximum, 'Downloaded file exceeds its size limit.')
    return data


def published(version):
    tag = 'v' + version + '-preview.1'
    metadata = json.loads(download(f'https://api.github.com/repos/{REPO}/releases/tags/{tag}', 1_048_576))
    require(metadata['tag_name'] == tag and metadata['immutable'] and not metadata['draft'],
            'Expected a published immutable release.')
    name = f'Bricklasaren-{version}-macOS-AppleSilicon.zip'
    matches = [a for a in metadata['assets'] if a['name'] == name]
    require(len(matches) == 1, 'Release ZIP is missing or ambiguous.')
    asset = matches[0]
    expected = f'https://github.com/{REPO}/releases/download/{tag}/{name}'
    require(asset['browser_download_url'] == expected, 'Unexpected immutable package URL.')
    data = download(expected, 52_428_800)
    require(len(data) == asset['size'] and 'sha256:' + hashlib.sha256(data).hexdigest() == asset['digest'],
            'Published package does not match GitHub metadata.')
    return asset, data


def verify_feed(envelope):
    header, encoded, signature = envelope.splitlines()
    require(header == b'BRICKLASARE-UPDATE-1', 'Unexpected feed envelope.')
    payload = base64.b64decode(encoded, validate=True)
    signed = base64.b64decode(signature, validate=True)
    # Independent public-key verification before executing a downloaded package.
    # The real 3.5.2 helper repeats signature verification using its compiled key.
    source = (pathlib.Path(__file__).resolve().parent.parent / 'Program/Updates.cs').read_text(encoding='utf-8-sig')
    xml, = re.findall(r'<RSAKeyValue>.*?</RSAKeyValue>', source)
    key = ET.fromstring(xml)
    n = int.from_bytes(base64.b64decode(key.findtext('Modulus'), validate=True), 'big')
    e = int.from_bytes(base64.b64decode(key.findtext('Exponent'), validate=True), 'big')
    require(n.bit_length() == 3072 and e == 65537 and len(signed) == 384
            and int.from_bytes(signed, 'big') < n, 'Unexpected RSA key or signature.')
    digest = bytes.fromhex('3031300d060960864801650304020105000420') + hashlib.sha256(payload).digest()
    expected = b'\x00\x01' + b'\xff' * (384 - len(digest) - 3) + b'\x00' + digest
    require(pow(int.from_bytes(signed, 'big'), e, n).to_bytes(384, 'big') == expected, 'Public feed signature failed.')
    feed = json.loads(payload)
    require(feed['Schema'] == 1 and feed['Channel'] == 'preview' and feed['Mac']['Version'] == TARGET,
            'This test requires the published 3.6.0 preview feed.')
    return feed['Mac']


def tree(path):
    result = {}
    for file in sorted(path.rglob('*')):
        require(not file.is_symlink(), 'Unexpected link in test app bundle.')
        if file.is_file():
            result[str(file.relative_to(path))] = (hashlib.sha256(file.read_bytes()).hexdigest(), file.stat().st_mode & 0o111)
    require(result, 'Application bundle is empty.')
    return result


def processes(roots):
    library = ctypes.CDLL('/usr/lib/libproc.dylib')
    library.proc_pidpath.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32]
    library.proc_pidpath.restype = ctypes.c_int
    result = []
    for value in command(['/bin/ps', '-ax', '-o', 'pid=']).split():
        pid = int(value)
        buffer = ctypes.create_string_buffer(4096)
        if library.proc_pidpath(pid, buffer, len(buffer)) > 0:
            path = pathlib.Path(os.fsdecode(buffer.value)).resolve()
            if any(path.is_relative_to(root) for root in roots):
                result.append((pid, path))
    return result


def stop_processes(roots):
    forced = False
    for sig in (signal.SIGTERM, signal.SIGKILL):
        found = processes(roots)
        if sig == signal.SIGKILL and found:
            forced = True
        for pid, _ in found:
            try:
                os.kill(pid, sig)
            except ProcessLookupError:
                pass
        if not found:
            return not forced
        time.sleep(1)
    require(not processes(roots), 'A test-owned child process did not stop.')
    return not forced


def main():
    runner_temp, data_parent = guard_runner()  # No filesystem mutation before this guard.
    report_dir = runner_temp / 'bricklasare-published-mac-update'
    report_dir.mkdir(exist_ok=False)
    work = pathlib.Path(tempfile.mkdtemp(prefix='bricklasare-mac-update-', dir=runner_temp)).resolve()
    data = data_parent / 'Bricklasaren'
    data.mkdir(parents=True, mode=0o700)
    result = {'source': SOURCE, 'target': TARGET, 'passed': False, 'checks': [],
              'scope': 'Published packages and actual installer on disposable Apple Silicon runner; no reader/tag or OS publisher-warning test.'}
    owned_roots = (work, data.resolve())
    try:
        envelope = download(f'https://raw.githubusercontent.com/{REPO}/{FEED_COMMIT}/Updates/preview.feed', 65_536)
        require(hashlib.sha256(envelope).hexdigest() == FEED_SHA256, 'Original signed 3.6.0 feed changed.')
        offered = verify_feed(envelope)
        old_asset, old_zip = published(SOURCE)
        new_asset, new_zip = published(TARGET)
        require(hashlib.sha256(old_zip).hexdigest() == SOURCE_SHA256, 'Prior installer differs from its pinned immutable package.')
        require(offered['Url'] == new_asset['browser_download_url'] and offered['Size'] == len(new_zip)
                and offered['Sha256'] == hashlib.sha256(new_zip).hexdigest(), 'Feed and immutable target package disagree.')
        result['checks'].append('Anonymous public feed and both immutable ZIP downloads; signed feed and server hashes verified')
        for name, content in [('old.zip', old_zip), ('new.zip', new_zip)]:
            (work / name).write_bytes(content)
        command(['/usr/bin/ditto', '-x', '-k', work / 'old.zip', work / 'install'])
        command(['/usr/bin/ditto', '-x', '-k', work / 'new.zip', work / 'expected'])
        target = work / 'install/Bricklasaren.app'
        binary = target / 'Contents/MacOS/Bricklasaren'
        command(['/usr/bin/codesign', '--verify', '--deep', '--strict', target])
        original_tree = tree(target)
        expected_tree = tree(work / 'expected/Bricklasaren.app')
        settings = {
            'formatval.json': b'{"Mode":"RCO","RcoChip":"E"}',
            'arbetsflode.json': b'{"copy":0,"sounds":false,"channel":"preview","autoCheck":false}',
            'utseende.json': b'{"Schema":1,"AccentHex":"#003B55","Organization":"","Subtitle":"","LogoFile":""}',
            'bevara-test.txt': b'Synthetic setting must survive the update.\n',
        }
        for name, content in settings.items():
            (data / name).write_bytes(content)
        updates = data / 'updates'
        updates.mkdir(mode=0o700)
        job = updates / str(uuid.uuid4())
        job.mkdir(mode=0o700)
        helper = job / 'installer'
        shutil.copy2(binary, helper)  # Exact old executable, no trust or code substitution.
        helper_hash = hashlib.sha256(helper.read_bytes()).hexdigest()
        plan = job / 'plan.json'

        def install_case(name, feed, package, expected_success):
            (job / 'release.feed').write_bytes(feed)
            (job / 'package.zip').write_bytes(package)
            parent = subprocess.Popen(['/bin/sleep', '0.5'])
            plan.write_text(json.dumps(dict(target=str(target), directory=str(job), parent=parent.pid, channel='preview')))
            child = None
            try:
                child = subprocess.Popen([str(helper), '--install-update', str(plan)],
                                         stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                         start_new_session=True)
                # Reap the simulated app as it exits: a zombie PID would otherwise
                # appear alive to the real helper's kill(parent, 0) handoff check.
                parent.wait(timeout=5)
                stdout, stderr = child.communicate(timeout=180)
                (report_dir / (name + '.log')).write_bytes(stdout + stderr)
                require(child.returncode == (0 if expected_success else 1),
                        name + ': unexpected installer result (crash/termination never counts as rejection).')
            finally:
                if parent.poll() is None:
                    parent.terminate()
                parent.wait(timeout=5)
                if child is not None and child.poll() is None:
                    # Also stop inherited system-tool children (ditto/codesign),
                    # whose executable paths are outside our temporary app roots.
                    try:
                        os.killpg(child.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    child.communicate(timeout=5)
            require(hashlib.sha256(helper.read_bytes()).hexdigest() == helper_hash, 'Installer bytes changed during the test.')
            for setting, before in settings.items():
                require((data / setting).read_bytes() == before, 'An existing synthetic setting changed.')
            if not expected_success:
                require(tree(target) == original_tree and not list(target.parent.glob('Bricklasaren.previous-*.app')),
                        name + ': rejection modified the original application.')
            result['checks'].append(name)

        invalid_feed = bytearray(envelope)
        invalid_feed[40] ^= 1
        install_case('Tampered signed feed rejected by the actual prior installer', bytes(invalid_feed), new_zip, False)
        changed_zip = bytearray(new_zip)
        changed_zip[len(changed_zip) // 2] ^= 1
        install_case('Changed release ZIP rejected without replacing the old app', envelope, bytes(changed_zip), False)
        pending = data / 'aterstall-synthetic.json'
        pending.write_text('{}')
        try:
            install_case('Pending recovery blocks replacement and preserves settings', envelope, new_zip, False)
        finally:
            pending.unlink()
        install_case('Actual 3.5.2 helper installs the signed 3.6.0 package', envelope, new_zip, True)
        require(tree(target) == expected_tree, 'Installed app differs from the signed release package.')
        backups = list(target.parent.glob('Bricklasaren.previous-*.app'))
        require(len(backups) == 1 and tree(backups[0]) == original_tree, 'Original app was not preserved exactly.')
        command(['/usr/bin/codesign', '--verify', '--deep', '--strict', target])
        require((data / 'uppdatering-status.txt').read_text().startswith('Uppdatering installerad.'), 'Installer did not report completion.')
        deadline = time.monotonic() + 15
        while not any(path == binary for _, path in processes(owned_roots)) and time.monotonic() < deadline:
            time.sleep(0.2)
        require(any(path == binary for _, path in processes(owned_roots)), 'Updated application did not start.')
        with (data / 'app.lock').open('r+b') as lock:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                pass
            else:
                fcntl.flock(lock, fcntl.LOCK_UN)
                raise RuntimeError('The restarted application did not hold its normal application lock.')
        result['checks'].append('Exact new bundle, exact old backup, codesign, settings and normal app restart verified')
        result['passed'] = True
    except Exception as error:
        result['error'] = str(error)
        raise
    finally:
        try:
            graceful = stop_processes(owned_roots)
            shutil.rmtree(work)
            shutil.rmtree(data_parent)
            result['cleanup'] = 'Only test-created app/settings and their child processes removed'
            require(graceful, 'Cleanup required SIGKILL; this run is not considered a passing update test.')
        except Exception as error:
            result['passed'] = False
            result['cleanup_error'] = str(error)
            raise
        finally:
            (report_dir / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
            print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
