"""Download the pinned official OBS release and extract only the runtime parts MonoClip needs into runtime/.

The archive is verified against a pinned SHA256 before anything is extracted. The result is bundled
into the portable package by RuntimeContent.props, so end users never download anything themselves.
"""
import hashlib, json, os, pathlib, shutil, tempfile, urllib.request, zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
VERSION = "32.2.2"
SHA256 = "4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1"
URL = f"https://github.com/obsproject/obs-studio/releases/download/{VERSION}/OBS-Studio-{VERSION}-Windows-x64.zip"
# obs-ffmpeg contains the replay buffer; obs-outputs (RTMP/FTL streaming) is never loaded.
MODULES = {"win-capture", "win-wasapi", "obs-ffmpeg", "obs-nvenc"}
# Frontend, scripting and UI-toolkit files are not used by libobs-only hosting. Also not imported or
# loaded by anything MonoClip uses: WebRTC (datachannel, msquic), Lua, OpenGL renderer, QSV test.
# Keep libobs-winrt.dll: win-capture loads it at runtime for Windows Graphics Capture.
SKIP = ["qt6", "obs64.exe", "obspython", "obslua", "python", "imageformats/", "platforms/", "styles/", "sqldrivers/", "tls/",
        "datachannel", "msquic", "lua51", "obs-scripting", "obs-frontend-api", "libobs-opengl", "obs-qsv-test"]
# MonoClip's UI is English; other module locales only add files.
LOCALES = {"en-US.ini"}
REQUIRED = ["bin/64bit/obs.dll", "bin/64bit/obs-ffmpeg-mux.exe", "bin/64bit/libobs-d3d11.dll", "data/libobs/default.effect", "obs-plugins/64bit/win-capture.dll"]


def wanted(name: str) -> bool:
    lower = name.lower()
    if lower.endswith(".pdb") or name.endswith("/"):
        return False
    if "/locale/" in name and pathlib.PurePosixPath(name).name not in LOCALES:
        return False
    if name.startswith("bin/64bit/"):
        return "/" not in name[len("bin/64bit/"):] and not any(x in lower for x in SKIP)
    if name.startswith("data/libobs/"):
        return True
    return any(name.startswith(f"data/obs-plugins/{m}/") or name == f"obs-plugins/64bit/{m}.dll" for m in MODULES)


def archive() -> pathlib.Path:
    cache = pathlib.Path(os.environ.get("MONOCLIP_OBS_CACHE", tempfile.gettempdir())) / f"monoclip-obs-{VERSION}.zip"
    if cache.exists() and hashlib.sha256(cache.read_bytes()).hexdigest() != SHA256:
        cache.unlink()  # stale or truncated download
    if not cache.exists():
        partial = cache.with_suffix(".part")
        print("Downloading", URL)
        urllib.request.urlretrieve(URL, partial)
        partial.replace(cache)
    sha = hashlib.sha256(cache.read_bytes()).hexdigest()
    if sha != SHA256:
        cache.unlink()
        raise SystemExit(f"OBS checksum mismatch: {sha}")
    print("OBS archive SHA256 verified:", sha)
    return cache


def main() -> None:
    zip_path = archive()
    rt = ROOT / "runtime"
    if rt.exists():
        shutil.rmtree(rt)  # never ship leftovers from an older runtime
    count = 0
    with zipfile.ZipFile(zip_path) as z:
        for entry in z.infolist():
            if entry.is_dir() or not wanted(entry.filename):
                continue
            dest = (rt / entry.filename).resolve()
            if not dest.is_relative_to(rt.resolve()):
                raise SystemExit("Unsafe archive path: " + entry.filename)
            dest.parent.mkdir(parents=True, exist_ok=True)
            with z.open(entry) as source, dest.open("wb") as out:
                shutil.copyfileobj(source, out)
            count += 1
        license_entry = "data/obs-studio/license/gplv2.txt"
        (rt / "OBS-LICENSE-gplv2.txt").write_bytes(z.read(license_entry))
    missing = [name for name in REQUIRED if not (rt / name).is_file()]
    if missing:
        raise SystemExit("Runtime incomplete: " + ", ".join(missing))
    (rt / "OBS-ORIGIN.json").write_text(json.dumps({"version": VERSION, "url": URL, "sha256": SHA256, "modules": sorted(MODULES), "files": count}, indent=2))
    print("Runtime files", count)
    print("Runtime bytes", sum(p.stat().st_size for p in rt.rglob("*") if p.is_file()))


if __name__ == "__main__":
    main()
