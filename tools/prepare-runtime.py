import urllib.request, json, hashlib, zipfile, pathlib, shutil
ROOT=pathlib.Path(__file__).resolve().parents[1]
zip_path=pathlib.Path.home()/"AppData/Local/Temp/monoclip-obs.zip"
release=json.load(urllib.request.urlopen("https://api.github.com/repos/obsproject/obs-studio/releases/tags/32.2.2"))
asset=next(x for x in release["assets"] if x["name"] == "OBS-Studio-32.2.2-Windows-x64.zip")
if not zip_path.exists():
    urllib.request.urlretrieve(asset["browser_download_url"], zip_path)
digest=asset.get("digest")
sha=hashlib.sha256(zip_path.read_bytes()).hexdigest()
if digest and digest != "sha256:"+sha: raise SystemExit("OBS checksum mismatch")
print("OBS archive SHA256",sha,"matches",digest)
modules={"win-capture", "win-wasapi", "obs-ffmpeg", "obs-outputs", "obs-nvenc"}
rt=ROOT/"runtime"
for stale in rt.rglob("*.pdb"): stale.unlink()
with zipfile.ZipFile(zip_path) as z:
    for f in z.infolist():
        name=f.filename
        include=(name.startswith("bin/64bit/") and not any(x in name.lower() for x in ["qt6", "obs64.exe", "obspython", "obslua", "python", "imageformats/", "platforms/", "styles/", "sqldrivers/", "tls/", ".pdb"])) or name.startswith("data/libobs/") or any(name.startswith("data/obs-plugins/"+m+"/") or name=="obs-plugins/64bit/"+m+".dll" for m in modules)
        if include and not f.is_dir() and not name.lower().endswith(".pdb"):
            dest=rt/name
            dest.parent.mkdir(parents=True,exist_ok=True)
            with z.open(f) as source, dest.open("wb") as out: shutil.copyfileobj(source,out)
(rt/"OBS-ORIGIN.json").write_text(json.dumps({"version":"32.2.2","url":asset["browser_download_url"],"sha256":sha,"modules":sorted(modules)},indent=2))
print("Runtime files", sum(1 for p in rt.rglob("*") if p.is_file()))
print("Runtime bytes", sum(p.stat().st_size for p in rt.rglob("*") if p.is_file()))
