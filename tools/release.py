#!/usr/bin/env python3
"""Validate local release packages and sign a feed; never upload or publish it."""
import argparse
import base64
import hashlib
import io
import json
import os
import pathlib
import plistlib
import re
import stat
import subprocess
import tempfile
import zipfile
import zlib

MAX_PACKAGE = 52_428_800
MAX_EXPANDED = 104_857_600


class ReleaseError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise ReleaseError(message)


def openssl(arguments, data=None):
    # Never include OpenSSL stderr/stdout in exceptions: a key path or its contents
    # must not reach release logs. Empty passphrase prevents interactive prompting.
    try:
        result = subprocess.run(['openssl', *arguments], input=data,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
    except (OSError, subprocess.TimeoutExpired):
        raise ReleaseError('OpenSSL could not complete the local key/signature check.') from None
    require(result.returncode == 0, 'OpenSSL rejected the key or signature; no feed was written.')
    return result.stdout


def match_one(pattern, source, description):
    values = re.findall(pattern, source)
    require(len(values) == 1, 'Missing or ambiguous ' + description + '.')
    return values[0]


def der(tag, value):
    size = len(value)
    encoded_size = bytes([size]) if size < 128 else size.to_bytes((size.bit_length() + 7) // 8, 'big')
    if size >= 128:
        encoded_size = bytes([128 + len(encoded_size)]) + encoded_size
    return bytes([tag]) + encoded_size + value


def xml_public_key(source):
    fields = match_one(r'<RSAKeyValue><Modulus>([A-Za-z0-9+/=]+)</Modulus>'
                       r'<Exponent>([A-Za-z0-9+/=]+)</Exponent></RSAKeyValue>',
                       source, 'pinned RSA public key')
    modulus, exponent = (base64.b64decode(field, validate=True) for field in fields)
    require(len(modulus) == 384 and int.from_bytes(modulus, 'big').bit_length() == 3072
            and exponent == b'\x01\x00\x01', 'Expected the existing RSA-3072 public key.')
    integers = [der(2, (b'\0' if value[0] & 128 else b'') + value)
                for value in (modulus, exponent)]
    return der(0x30, b''.join(integers))


def pinned_public_key(repo):
    windows = xml_public_key((repo / 'Program/Updates.cs').read_text(encoding='utf-8-sig'))
    installer = xml_public_key((repo / 'Program/Install-Update.ps1').read_text(encoding='utf-8-sig'))
    mac_source = (repo / 'Mac/Sources/Updates.swift').read_text(encoding='utf-8')
    mac = base64.b64decode(match_one(r'static let publicKey\s*=\s*"([A-Za-z0-9+/=]+)"',
                                    mac_source, 'Mac public key'), validate=True)
    require(windows == installer == mac, 'The installed public keys disagree; no feed was written.')
    return windows


def read_bounded(path, limit):
    require(stat.S_ISREG(path.stat().st_mode), 'Expected a regular local file.')
    with path.open('rb') as stream:
        data = stream.read(limit + 1)
    require(0 < len(data) <= limit, 'File is empty or exceeds the supported size limit.')
    return data


def validate_zip(data, platform, version):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entries = archive.infolist()
        require(0 < len(entries) <= 1000, 'Unsupported ZIP entry count.')
        require(sum(item.file_size for item in entries) <= MAX_EXPANDED,
                'ZIP expands beyond the updater limit.')
        names = set()
        for item in entries:
            name = item.filename
            parts = name.rstrip('/').split('/')
            require(name and item.orig_filename == name and '\\' not in name
                    and all(part and part not in ('.', '..') and part.rstrip(' .') == part
                            and not re.search(r'[<>:"|?*\x00-\x1f]', part)
                            and not re.fullmatch(r'(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?', part)
                            for part in parts)
                    and name.casefold() not in names, 'Unsafe or duplicate ZIP path.')
            names.add(name.casefold())
            kind = stat.S_IFMT(item.external_attr >> 16)
            require(kind in (0, stat.S_IFREG, stat.S_IFDIR)
                    and not item.flag_bits & 1
                    and item.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED),
                    'ZIP contains links, encryption, or an unsupported entry.')
            # Read every entry to catch CRC errors and local/central name conflicts.
            with archive.open(item) as stream:
                count = 0
                while chunk := stream.read(65_536):
                    count += len(chunk)
                    require(count <= item.file_size, 'Invalid expanded ZIP size.')
                require(count == item.file_size, 'Truncated ZIP entry.')

        def member(name):
            try:
                info = archive.getinfo(name)
            except KeyError:
                raise ReleaseError('ZIP is missing required application files.') from None
            require(not info.is_dir() and 0 < info.file_size <= 1_048_576,
                    'Invalid application metadata size.')
            return archive.read(info)

        if platform == 'Windows':
            member('Starta-Bricklasaren.cmd')
            member('Program/Starta-Bricklasaren.ps1')
            source = member('Program/Entry.cs').decode('utf-8-sig')
            for field in ('AssemblyVersion', 'AssemblyFileVersion'):
                found = match_one(r'\[assembly:\s*System\.Reflection\.' + field + r'\("([^"]+)"\)\]',
                                  source, field)
                require(found == version + '.0', 'Windows package version does not match the feed.')
            source = member('Program/Updates.cs').decode('utf-8-sig')
            require(match_one(r'internal const string Current\s*=\s*"([^"]+)"', source,
                              'Windows updater version') == version, 'Windows updater version mismatch.')
            source = member('Program/Install-Update.ps1').decode('utf-8-sig')
            require(match_one(r"\$CurrentVersion\s*=\s*'([^']+)'", source,
                              'Windows installer version') == version, 'Windows installer version mismatch.')
        else:
            prefix = 'Bricklasaren.app/Contents/'
            info = plistlib.loads(member(prefix + 'Info.plist'))
            require(isinstance(info, dict) and info.get('CFBundleShortVersionString') == version
                    and info.get('CFBundleIdentifier') == 'se.tobtech.bricklasaren'
                    and info.get('CFBundleExecutable') == 'Bricklasaren',
                    'Mac package identity or version does not match the feed.')
            require(prefix + 'MacOS/Bricklasaren' in archive.namelist()
                    and archive.getinfo(prefix + 'MacOS/Bricklasaren').file_size > 0,
                    'Mac application executable is missing.')


def asset(path, platform, args):
    if path is None:
        return None
    suffix = 'Windows' if platform == 'Windows' else 'macOS-AppleSilicon'
    require(path.name == f'Bricklasaren-{args.version}-{suffix}.zip',
            'Package filename does not match its platform and version.')
    data = read_bounded(path, MAX_PACKAGE)
    validate_zip(data, platform, args.version)
    return dict(Version=args.version,
                Url=f'https://github.com/tstenkula/bricklasare/releases/download/{args.tag}/{path.name}',
                Sha256=hashlib.sha256(data).hexdigest(), Size=len(data), Notes=args.notes)


def sign(args, repo):
    require(re.fullmatch(r'(?:0|[1-9][0-9]{0,4})(?:\.(?:0|[1-9][0-9]{0,4})){2}', args.version)
            and all(int(part) <= 65535 for part in args.version.split('.')), 'Invalid release version.')
    expected_tag = 'v' + re.escape(args.version)
    if args.channel == 'preview':
        expected_tag += r'-preview\.[1-9][0-9]{0,5}'
    require(re.fullmatch(expected_tag, args.tag), 'Tag does not match the version and channel.')
    require(args.windows or args.mac, 'Supply at least one release ZIP.')
    require(len(args.notes.encode('utf-16-le')) // 2 <= 4000, 'Release notes exceed the client limit.')
    require(args.output.name == args.channel + '.feed', 'Output filename must match the channel.')
    key = args.key.resolve()
    require(not key.is_relative_to(repo.resolve()), 'Private signing key must be outside the repository.')
    require(stat.S_ISREG(key.stat().st_mode) and not key.stat().st_mode & 0o077,
            'Signing key must be a regular owner-only file (chmod 600).')
    require(args.output.resolve() not in {key, *(p.resolve() for p in (args.windows, args.mac) if p)},
            'Feed output must not overwrite a signing key or package.')
    public_key = pinned_public_key(repo)
    derived = openssl(['rsa', '-in', str(key), '-passin', 'pass:', '-RSAPublicKey_out', '-outform', 'DER'])
    require(derived == public_key, 'Signing key does not match the installed public key.')
    payload = json.dumps(dict(Schema=1, Channel=args.channel,
                              Windows=asset(args.windows, 'Windows', args),
                              Mac=asset(args.mac, 'Mac', args)), ensure_ascii=False,
                         sort_keys=True, separators=(',', ':')).encode('utf-8')
    signature = openssl(['dgst', '-sha256', '-sign', str(key), '-passin', 'pass:'], payload)
    # Verify against the pin again, including if the key file changed between calls.
    with tempfile.TemporaryDirectory(prefix='bricklasare-release-') as temporary:
        public = pathlib.Path(temporary) / 'public.pem'
        public.write_bytes(b'-----BEGIN RSA PUBLIC KEY-----\n' + base64.b64encode(public_key)
                           + b'\n-----END RSA PUBLIC KEY-----\n')
        signature_path = pathlib.Path(temporary) / 'signature'
        signature_path.write_bytes(signature)
        openssl(['dgst', '-sha256', '-verify', str(public), '-signature', str(signature_path)], payload)
    envelope = (b'BRICKLASARE-UPDATE-1\n' + base64.b64encode(payload) + b'\n'
                + base64.b64encode(signature) + b'\n')
    require(len(envelope) <= 65536, 'Signed feed exceeds the client limit.')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    staged = None
    try:
        with tempfile.NamedTemporaryFile(dir=args.output.parent, prefix='.release-', delete=False) as output:
            staged = pathlib.Path(output.name)
            output.write(envelope)
        os.replace(staged, args.output)
    finally:
        if staged is not None:
            staged.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--key', required=True, type=pathlib.Path)
    parser.add_argument('--channel', required=True, choices=['stable', 'preview'])
    parser.add_argument('--version', required=True)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--windows', type=pathlib.Path)
    parser.add_argument('--mac', type=pathlib.Path)
    parser.add_argument('--notes', default='Tobtech Brickläsaren: arbetsflöde, felsökning och verifierade uppdateringar.')
    parser.add_argument('--output', required=True, type=pathlib.Path)
    args = parser.parse_args()
    try:
        sign(args, pathlib.Path(__file__).resolve().parent.parent)
    except ReleaseError as error:
        parser.error(str(error))
    except (ValueError, OSError, zipfile.BadZipFile, RuntimeError, EOFError, zlib.error):
        parser.error('Could not validate local release files; no feed was written.')
    print('Signed', args.channel, 'feed:', args.output)


if __name__ == '__main__':
    main()
