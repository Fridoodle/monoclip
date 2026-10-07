import pathlib,zipfile,json,hashlib,shutil
root=pathlib.Path(__file__).resolve().parents[1]
app=pathlib.Path.home()/"Apps/MonoClip"
for name in ["README.md","VERIFICATION.md","THIRD-PARTY.md","LICENSE"]:shutil.copy2(root/name,app/name)
shutil.copytree(root/"licenses",app/"licenses",dirs_exist_ok=True,ignore=shutil.ignore_patterns("native"))
shutil.copy2(root/"runtime/OBS-ORIGIN.json",app/"OBS-ORIGIN.json")
out=root/"dist";out.mkdir(exist_ok=True)
with zipfile.ZipFile(out/"MonoClip-0.1.2-beta-win-x64.zip","w",zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in app.rglob("*"):
        if p.is_file() and p.suffix not in {".pdb",".log"}:z.write(p,pathlib.Path("MonoClip")/p.relative_to(app))
with zipfile.ZipFile(out/"MonoClip-0.1.2-beta-source.zip","w",zipfile.ZIP_DEFLATED) as z:
    for folder in ["src","tests","tools","licenses","verification"]:
        for p in (root/folder).rglob("*"):
            if not p.is_file() or any(x in p.parts for x in ["bin","obj","__pycache__"]): continue
            if folder=="licenses" and "native" in p.relative_to(root/"licenses").parts:continue
            if folder=="verification" and (p.suffix not in {".json",".md",".txt",".log"} or "runtime" in p.parts or any(part.startswith("native") for part in p.relative_to(root/"verification").parts[:-1])):continue
            if p.suffix not in {".mkv",".mp4"}:z.write(p,pathlib.Path("MonoClip-source")/p.relative_to(root))
    for name in ["README.md","VERIFICATION.md","THIRD-PARTY.md","LICENSE","RuntimeContent.props","build.ps1",".gitignore"]:z.write(root/name,pathlib.Path("MonoClip-source")/name)
result={}
for filename in ["MonoClip-0.1.2-beta-win-x64.zip","MonoClip-0.1.2-beta-source.zip"]:
    p=out/filename
    with zipfile.ZipFile(p) as z:assert z.testzip() is None
    result[filename]={"bytes":p.stat().st_size,"sha256":hashlib.sha256(p.read_bytes()).hexdigest()}
(out/"checksums.json").write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
