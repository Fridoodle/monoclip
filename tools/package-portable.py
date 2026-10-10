"""Zip a published MonoClip folder into ONE portable download: unzip, start MonoClip.exe, done.

Everything is inside: the self-contained .NET app and the OBS capture runtime. No setup script,
no second download, no installer and no administrator rights.

Layout: MonoClip.exe (single-file starter) plus folders only. The app runs from app\ because OBS
finds its helper programs next to the running EXE; notices and licenses live in info\.
"""
import argparse, hashlib, json, pathlib, re, shutil, zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
APP = ["MonoClip.exe", "obs.dll", "obs-ffmpeg-mux.exe", "obs-amf-test.exe", "libobs-d3d11.dll", "runtime/data/libobs/default.effect",
       "runtime/obs-plugins/64bit/win-capture.dll", "runtime/data/obs-plugins/win-capture/graphics-hook64.dll"]
REQUIRED = ["MonoClip.exe"] + ["app/" + name for name in APP]


def project_version() -> str:
    text = (ROOT / "src/MonoClip.Windows/MonoClip.Windows.csproj").read_text(encoding="utf-8-sig")
    return re.search(r"<Version>([^<]+)</Version>", text).group(1)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--app", type=pathlib.Path, default=ROOT / "dist/MonoClip", help="folder with the starter MonoClip.exe and the published app in app/")
    p.add_argument("--version", default=None, help="defaults to <Version> of MonoClip.Windows.csproj")
    p.add_argument("--out", type=pathlib.Path, default=ROOT / "dist")
    a = p.parse_args()
    version = a.version or project_version()
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[a-z0-9.-]+)?", version):
        raise SystemExit("Invalid release version: " + version)
    app = a.app.resolve()
    missing = [name for name in REQUIRED if not (app / name).is_file()]
    if missing:
        raise SystemExit("Publish output is not a complete portable app (run tools/prepare-runtime.py first). Missing: " + ", ".join(missing))
    # The point of the layout: a user opening the folder sees MonoClip.exe and folders, nothing else.
    loose = [f.name for f in app.iterdir() if f.is_file() and f.name != "MonoClip.exe" and f.suffix != ".pdb"]
    if loose:
        raise SystemExit("Only MonoClip.exe may sit in the folder root, found: " + ", ".join(sorted(loose)))

    # Notices travel with the binaries.
    runtime = ROOT / "runtime"
    extras = {"info/" + name: ROOT / name for name in ["README.md", "CHANGELOG.md", "THIRD-PARTY.md", "LICENSE"]}
    extras["info/OBS-ORIGIN.json"] = runtime / "OBS-ORIGIN.json"
    extras["info/licenses/OBS-LICENSE-gplv2.txt"] = runtime / "OBS-LICENSE-gplv2.txt"
    for file in (ROOT / "licenses").rglob("*"):
        if file.is_file() and "native" not in file.relative_to(ROOT / "licenses").parts:
            extras["info/licenses/" + file.relative_to(ROOT / "licenses").as_posix()] = file
    for name, source in extras.items():
        if not source.is_file():
            raise SystemExit("Missing notice file: " + str(source))

    a.out.mkdir(parents=True, exist_ok=True)
    output = a.out / f"MonoClip-{version}-win-x64-portable.zip"
    output.unlink(missing_ok=True)
    count = 0
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for file in sorted(app.rglob("*")):
            relative = file.relative_to(app)
            if not file.is_file() or file.suffix in {".pdb", ".log"} or relative.as_posix() in extras or file.suffix in {".ps1", ".cmd"}:
                continue
            z.write(file, pathlib.PurePosixPath("MonoClip") / relative.as_posix()); count += 1
        for name, source in extras.items():
            z.write(source, pathlib.PurePosixPath("MonoClip") / name); count += 1

    with zipfile.ZipFile(output) as z:
        if z.testzip() is not None:
            raise SystemExit("Corrupt zip")
        names = set(z.namelist())
    lost = [name for name in REQUIRED if "MonoClip/" + name not in names]
    if lost:
        raise SystemExit("Zip lost required files: " + ", ".join(lost))
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    (a.out / "SHA256SUMS.txt").write_text(f"{digest}  {output.name}\n")
    print(json.dumps({"file": output.name, "files": count, "bytes": output.stat().st_size, "sha256": digest}, indent=2))


if __name__ == "__main__":
    main()
