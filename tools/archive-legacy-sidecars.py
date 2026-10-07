"""Archive only validated MonoClip sidecars; never remove videos."""
import argparse, json, pathlib, os, shutil, datetime
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('root',type=pathlib.Path);p.add_argument('--apply',action='store_true')
a=p.parse_args();root=a.root.resolve();matches=[]
for file in root.rglob('*.mkv.json'):
    video=pathlib.Path(str(file)[:-5])
    if not video.is_file() or file.stat().st_size>64*1024:continue
    try:
        record=json.loads(file.read_text(encoding='utf-8-sig'))
        required={'Path','Game','SavedAt','RequestedSeconds','Width','Height','Fps','Encoder','Context'}
        if not isinstance(record,dict) or not required.issubset(record):continue
        if pathlib.PureWindowsPath(record['Path']).name!=video.name:continue
        if record['Fps'] not in [30,60,120] or record['Width'] not in [1280,1920,2560,3840]:continue
        if not isinstance(record['Context'],dict) or 'CaptureMode' not in record['Context']:continue
        matches.append(file)
    except (OSError,ValueError,TypeError):continue
backup=pathlib.Path(os.environ.get('LOCALAPPDATA',str(pathlib.Path.home())))/'MonoClip'/'legacy-sidecars'/datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
if a.apply:
    for file in matches:
        target=backup/file.relative_to(root);target.parent.mkdir(parents=True,exist_ok=True)
        if target.exists():raise SystemExit('Backup collision; original left untouched')
        shutil.move(str(file),str(target))
print(json.dumps({'mode':'archived' if a.apply else 'preview','matched_sidecars':len(matches),'backup':str(backup) if a.apply and matches else None},indent=2))
