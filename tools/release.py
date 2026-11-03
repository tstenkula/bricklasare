#!/usr/bin/env python3
"""Sign a public update feed. The private key stays outside the repository."""
import argparse, base64, hashlib, json, pathlib, subprocess, tempfile
p=argparse.ArgumentParser()
p.add_argument('--key', required=True, type=pathlib.Path)
p.add_argument('--channel', required=True, choices=['stable','preview'])
p.add_argument('--version', default='3.5.2')
p.add_argument('--tag', default='v3.5.2-preview.1')
p.add_argument('--windows', type=pathlib.Path)
p.add_argument('--mac', type=pathlib.Path)
p.add_argument('--notes', default='Tobtech Brickläsaren: arbetsflöde, felsökning och verifierade uppdateringar.')
p.add_argument('--output', required=True, type=pathlib.Path)
a=p.parse_args()
repo=pathlib.Path(__file__).resolve().parent.parent
if a.key.resolve().is_relative_to(repo): p.error('Private signing key must be outside the repository')
if not a.key.is_file(): p.error('Signing key not found')
if a.key.stat().st_mode & 0o077: p.error('Signing key must be readable only by its owner (chmod 600)')
def asset(path):
 if path is None: return None
 data=path.read_bytes()
 return dict(Version=a.version, Url='https://github.com/tstenkula/bricklasare/releases/download/'+a.tag+'/'+path.name, Sha256=hashlib.sha256(data).hexdigest(), Size=len(data), Notes=a.notes)
payload=json.dumps(dict(Schema=1,Channel=a.channel,Windows=asset(a.windows),Mac=asset(a.mac)),ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()
signature=subprocess.check_output(['openssl','dgst','-sha256','-sign',str(a.key)],input=payload)
a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text('BRICKLASARE-UPDATE-1\n'+base64.b64encode(payload).decode()+'\n'+base64.b64encode(signature).decode()+'\n')
print('Signed', a.channel, 'feed:', a.output)
