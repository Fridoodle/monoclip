"""Package MonoClip/.NET only; native OBS files are fetched by the end user."""
import pathlib,shutil,zipfile,hashlib,json,argparse
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--version',required=True);a=p.parse_args()
import re
assert re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[a-z0-9.-]+)?',a.version), 'Invalid release version'
app=root/'dist/MonoClip-public';assert (app/'MonoClip.exe').is_file();assert not (app/'obs.dll').exists(),"Native OBS binaries must not be included in public bundle"
for name in ['README.md','CHANGELOG.md','THIRD-PARTY.md','LICENSE']:shutil.copy2(root/name,app/name)
shutil.copytree(root/'licenses',app/'licenses',dirs_exist_ok=True,ignore=shutil.ignore_patterns('native'))
shutil.copy2(root/'tools/install-runtime.ps1',app/'install-runtime.ps1')
shutil.copy2(root/'tools/runtime-setup.cmd',app/'Runtime einrichten.cmd')
output=root/f'dist/MonoClip-{a.version}-win-x64-setup.zip'
with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for file in app.rglob('*'):
        if file.is_file() and file.suffix not in ['.pdb','.log']:z.write(file,pathlib.Path('MonoClip')/file.relative_to(app))
source=root/f'dist/MonoClip-{a.version}-public-source.zip'
with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED) as z:
    for file in a.source.rglob('*'):
        if file.is_file() and not any(x in file.parts for x in ['.git','bin','obj','__pycache__']):z.write(file,pathlib.Path('MonoClip-source')/file.relative_to(a.source))
results={}
for file in [output,source]:
    with zipfile.ZipFile(file) as z:assert z.testzip() is None
    results[file.name]={'bytes':file.stat().st_size,'sha256':hashlib.sha256(file.read_bytes()).hexdigest()}
(root/'dist/public-checksums.json').write_text(json.dumps(results,indent=2))
(root/'dist/SHA256SUMS.txt').write_text(''.join(v['sha256']+'  '+k+'\n' for k,v in results.items()))
print(json.dumps(results,indent=2))
