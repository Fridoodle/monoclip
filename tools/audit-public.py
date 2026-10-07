"""Audit staged public files without printing potential secrets."""
import pathlib,subprocess,re
files=subprocess.check_output(['git','diff','--cached','--name-only'],text=True).splitlines()
home=str(pathlib.Path.home());variants={home,home.replace('\\','/'),home.replace('\\','\\\\')};bad=[];large=[]
for name in files:
    path=pathlib.Path(name)
    if not path.is_file():continue
    if path.stat().st_size>1000000:large.append(name)
    text=path.read_text(encoding='utf-8-sig')
    if any(value in text for value in variants) or re.search(r'(ghp_|gho_|sk-or-v1-)[A-Za-z0-9_]{20,}',text):bad.append(name)
assert not bad,'Possible private values in files: '+str(bad)
assert not large,'Unexpected large files: '+str(large)
assert not any(any(part in ['verification','runtime','bin','obj'] for part in pathlib.Path(name).parts) for name in files)
print('Staged public audit passed:',len(files),'files; no recordings, runtime binaries, local paths or recognised tokens')
