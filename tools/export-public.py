"""Export an allowlisted public source snapshot, without local history or recordings."""
import pathlib,shutil,argparse
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('destination',type=pathlib.Path);args=p.parse_args();dest=args.destination.resolve();dest.mkdir(parents=True,exist_ok=True)
for folder in ['src','tests','tools','licenses','.github']:
    for file in (root/folder).rglob('*'):
        if not file.is_file() or any(x in file.relative_to(root).parts for x in ['bin','obj','__pycache__','.audit-cache']):continue
        if folder=='licenses' and 'native' in file.relative_to(root/'licenses').parts:continue
        if file.suffix in {'.dll','.exe','.pdb','.mkv','.mp4','.wav','.zip','.log'}:continue
        if file.name in {'tdd-evidence.json','benchmark-native.json','results.json'}:continue
        target=dest/file.relative_to(root);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(file,target)
for name in ['README.md','CHANGELOG.md','CONTRIBUTING.md','THIRD-PARTY.md','LICENSE','RuntimeContent.props','build.ps1','.gitignore']:
    shutil.copy2(root/name,dest/name)
files=[p for p in dest.rglob('*') if p.is_file() and not any(x in p.parts for x in ['.git','bin','obj','__pycache__'])]
assert not any(p.suffix in {'.dll','.exe','.mkv','.mp4','.log'} for p in files)
leaks=[]
for file in files:
    try:text=file.read_text(encoding='utf-8-sig')
    except (UnicodeError,OSError):continue
    home=str(pathlib.Path.home());variants={home,home.replace('\\','/'),home.replace('\\','\\\\')}
    if any(value in text for value in variants):leaks.append(str(file.relative_to(dest)))
assert not leaks,'Machine-local paths in public export: '+str(leaks)
print('Public source snapshot:',dest,'files:',len(files),'no binaries, personal recordings or machine-local paths')
