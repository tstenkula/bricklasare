#!/usr/bin/env python3
"""Offline signer regression tests. All private keys and fixtures are temporary."""
import base64
import hashlib
import importlib.util
import io
import json
import pathlib
import plistlib
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import warnings
import zipfile

SCRIPT = pathlib.Path(__file__).with_name('release.py').resolve()
VERSION = '3.6.0'


def command(*arguments, data=None):
    result = subprocess.run(arguments, input=data, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=45)
    if result.returncode:
        raise AssertionError('Test fixture OpenSSL operation failed.')
    return result.stdout


class ReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.keys = tempfile.TemporaryDirectory(prefix='bricklasare-test-keys-')
        cls.addClassCleanup(cls.keys.cleanup)
        cls.key = pathlib.Path(cls.keys.name) / 'synthetic.pem'
        cls.other_key = pathlib.Path(cls.keys.name) / 'other-synthetic.pem'
        for key in (cls.key, cls.other_key):
            command('openssl', 'genpkey', '-algorithm', 'RSA', '-pkeyopt', 'rsa_keygen_bits:3072', '-out', str(key))
            key.chmod(0o600)
        cls.public = command('openssl', 'rsa', '-in', str(cls.key), '-RSAPublicKey_out', '-outform', 'DER')
        # Independent OpenSSL export constructs the XML fixture, not the signer's DER code.
        modulus = bytes.fromhex(command('openssl', 'rsa', '-in', str(cls.key), '-modulus', '-noout')
                                .decode().strip().split('=', 1)[1])
        cls.xml = ('<RSAKeyValue><Modulus>' + base64.b64encode(modulus).decode()
                   + '</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>')

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='bricklasare-release-test-')
        self.addCleanup(self.temporary.cleanup)
        self.repo = pathlib.Path(self.temporary.name) / 'repo'
        for name in ('tools', 'Program', 'Mac/Sources', 'Updates', 'dist'):
            (self.repo / name).mkdir(parents=True, exist_ok=True)
        shutil.copyfile(SCRIPT, self.repo / 'tools/release.py')
        (self.repo / 'Program/Updates.cs').write_text('internal const string PublicKey = "' + self.xml + '";')
        (self.repo / 'Program/Install-Update.ps1').write_text("$PublicKey = '" + self.xml + "'")
        (self.repo / 'Mac/Sources/Updates.swift').write_text(
            'static let publicKey = "' + base64.b64encode(self.public).decode() + '"')
        self.windows = self.repo / f'dist/Bricklasaren-{VERSION}-Windows.zip'
        self.mac = self.repo / f'dist/Bricklasaren-{VERSION}-macOS-AppleSilicon.zip'
        self.output = self.repo / 'Updates/preview.feed'
        self.previous = b'pre-existing feed must survive validation failure\n'
        self.output.write_bytes(self.previous)
        self.windows_files = {
            'Starta-Bricklasaren.cmd': b'nonexecutable launcher fixture',
            'Program/Starta-Bricklasaren.ps1': b'nonexecutable script fixture',
            'Program/Entry.cs': (f'[assembly: System.Reflection.AssemblyVersion("{VERSION}.0")]\n'
                                 f'[assembly: System.Reflection.AssemblyFileVersion("{VERSION}.0")]').encode(),
            'Program/Updates.cs': f'internal const string Current = "{VERSION}";'.encode(),
            'Program/Install-Update.ps1': f"$CurrentVersion = '{VERSION}'".encode(),
        }
        self.plist = dict(CFBundleShortVersionString=VERSION, CFBundleIdentifier='se.tobtech.bricklasaren',
                          CFBundleExecutable='Bricklasaren')
        self.mac_files = {
            'Bricklasaren.app/Contents/Info.plist': plistlib.dumps(self.plist),
            'Bricklasaren.app/Contents/MacOS/Bricklasaren': b'nonexecutable Mac fixture',
        }
        self.archive(self.windows, self.windows_files)
        self.archive(self.mac, self.mac_files)

    def archive(self, path, files):
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', UserWarning)
            with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_STORED) as archive:
                for name, data in (files.items() if isinstance(files, dict) else files):
                    archive.writestr(name, data)

    def run_signer(self, success=False, **overrides):
        values = dict(key=self.key, channel='preview', version=VERSION, tag=f'v{VERSION}-preview.1',
                      windows=self.windows, mac=self.mac, output=self.output)
        values.update(overrides)
        output = pathlib.Path(values['output'])
        before = output.read_bytes() if output.is_file() else None
        args = [sys.executable, str(self.repo / 'tools/release.py')]
        for name, value in values.items():
            if value is not None:
                args += ['--' + name, str(value)]
        result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45)
        self.assertEqual(result.returncode == 0, success, result.stderr.decode())
        self.assertNotIn(b'PRIVATE KEY', result.stdout + result.stderr)
        if not success:
            self.assertEqual(output.read_bytes() if output.is_file() else None, before,
                             'Failed validation changed the existing feed')
        return output

    def test_round_trip_both_platforms_and_unchanged_envelope(self):
        output = self.run_signer(success=True)
        header, encoded, signature = output.read_bytes().splitlines()
        self.assertEqual(header, b'BRICKLASARE-UPDATE-1')
        payload = base64.b64decode(encoded, validate=True)
        feed = json.loads(payload)
        self.assertEqual((feed['Schema'], feed['Channel']), (1, 'preview'))
        for name, path in (('Windows', self.windows), ('Mac', self.mac)):
            self.assertEqual(feed[name]['Version'], VERSION)
            self.assertEqual(feed[name]['Size'], path.stat().st_size)
            self.assertEqual(feed[name]['Sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertTrue(feed[name]['Url'].endswith('/' + path.name))
        public = self.repo / 'public.pem'
        public.write_bytes(command('openssl', 'rsa', '-in', str(self.key), '-pubout'))
        signature_path = self.repo / 'signature'
        signature_path.write_bytes(base64.b64decode(signature, validate=True))
        command('openssl', 'dgst', '-sha256', '-verify', str(public), '-signature', str(signature_path), data=payload)

    def test_stable_and_single_platform_remain_supported(self):
        output = self.run_signer(success=True, channel='stable', tag=f'v{VERSION}', mac=None,
                                 output=self.repo / 'Updates/stable.feed')
        feed = json.loads(base64.b64decode(output.read_bytes().splitlines()[1]))
        self.assertEqual(feed['Channel'], 'stable')
        self.assertIsNone(feed['Mac'])

    def test_wrong_key_and_public_pin_drift_are_rejected(self):
        self.run_signer(key=self.other_key)
        (self.repo / 'Mac/Sources/Updates.swift').write_text('static let publicKey = "AQAB"')
        self.run_signer()

    def test_key_inside_repo_and_world_readable_key_are_rejected(self):
        local = self.repo / 'accidental-key.pem'
        shutil.copyfile(self.key, local)
        local.chmod(0o600)
        self.run_signer(key=local)
        external = pathlib.Path(self.temporary.name) / 'readable-key.pem'
        shutil.copyfile(self.key, external)
        external.chmod(0o644)
        self.run_signer(key=external)

    def test_bad_versions_tags_channels_and_empty_release(self):
        cases = [dict(version=v) for v in ('03.6.0', '3.6', '3.6.0.0', '65536.0.0', '3.6.0/evil')]
        cases += [dict(tag=t) for t in ('v3.5.2-preview.1', f'v{VERSION}', f'v{VERSION}-preview.0', '../wrong')]
        cases += [dict(channel='stable'), dict(channel='typo'), dict(windows=None, mac=None)]
        for case in cases:
            with self.subTest(case=case):
                self.run_signer(**case)

    def test_mismatched_output_and_overlong_unicode_notes(self):
        self.run_signer(output=self.repo / 'Updates/stable.feed')
        self.run_signer(notes='😀' * 2001)

    def test_mislabeled_archives_and_windows_metadata(self):
        renamed = self.windows.with_name('WrongName.zip')
        shutil.copyfile(self.windows, renamed)
        self.run_signer(windows=renamed)
        for name in ('Program/Entry.cs', 'Program/Updates.cs', 'Program/Install-Update.ps1'):
            with self.subTest(metadata=name):
                files = dict(self.windows_files)
                files[name] = files[name].replace(VERSION.encode(), b'3.5.2')
                self.archive(self.windows, files)
                self.run_signer()
        self.archive(self.windows, self.mac_files)
        self.run_signer()

    def test_missing_files_and_mac_metadata_mismatch(self):
        for name, value in [('CFBundleShortVersionString', '3.5.2'), ('CFBundleIdentifier', 'wrong.app')]:
            with self.subTest(metadata=name):
                info = dict(self.plist)
                info[name] = value
                files = dict(self.mac_files)
                files['Bricklasaren.app/Contents/Info.plist'] = plistlib.dumps(info)
                self.archive(self.mac, files)
                self.run_signer()
        self.archive(self.mac, {'Bricklasaren.app/Contents/Info.plist': plistlib.dumps(self.plist)})
        self.run_signer()

    def test_traversal_duplicates_and_symlinks_rejected(self):
        for name in ('../outside', '/absolute', 'Program\\outside', 'Program/CON.txt', 'Program/Entry.cs'):
            with self.subTest(path=name):
                self.archive(self.windows, [*self.windows_files.items(), (name, b'bad')])
                self.run_signer()
        linked = zipfile.ZipInfo('Program/link')
        linked.create_system = 3
        linked.external_attr = 0o120777 << 16
        self.archive(self.windows, [*self.windows_files.items(), (linked, b'../outside')])
        self.run_signer()

    def test_nonzip_truncated_crc_and_size_limits(self):
        valid = self.windows.read_bytes()
        corrupt = bytearray(valid)
        offset = corrupt.index(b'nonexecutable launcher fixture')
        corrupt[offset] ^= 1
        expanded = bytearray(valid)
        central = expanded.index(b'PK\x01\x02')
        struct.pack_into('<I', expanded, central + 24, 104_857_601)
        for data in (b'not a zip', valid[:-10], corrupt, expanded):
            with self.subTest(archive_type=type(data).__name__, size=len(data)):
                self.windows.write_bytes(data)
                self.run_signer()
        with self.windows.open('wb') as file:
            file.seek(52_428_800)
            file.write(b'x')
        self.run_signer()

    def test_repository_public_pins_agree_without_opening_private_keys(self):
        spec = importlib.util.spec_from_file_location('release_validation', SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertGreater(len(module.pinned_public_key(SCRIPT.parent.parent)), 384)


if __name__ == '__main__':
    unittest.main(verbosity=2)
