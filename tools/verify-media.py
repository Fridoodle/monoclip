import pathlib,json,subprocess,argparse
parser=argparse.ArgumentParser(description="Verify a real clip without reading/writing sidecars")
parser.add_argument("root");parser.add_argument("--width",type=int);parser.add_argument("--height",type=int);parser.add_argument("--fps",type=int);parser.add_argument("--seconds",type=float)
args=parser.parse_args();root=pathlib.Path(args.root);file=max(root.rglob("*.mkv"),key=lambda p:p.stat().st_mtime)
data=json.loads(subprocess.check_output(["ffprobe","-v","error","-show_streams","-show_format","-of","json",str(file)]))
video=next(s for s in data["streams"] if s["codec_type"]=="video");audio=[s for s in data["streams"] if s["codec_type"]=="audio"]
assert video["codec_name"]=="h264"
if args.width:assert video["width"]==args.width
if args.height:assert video["height"]==args.height
if args.fps:assert video["avg_frame_rate"]==str(args.fps)+"/1"
duration=float(data["format"]["duration"])
if args.seconds:assert args.seconds-.25<=duration<=args.seconds+1.25,(duration,args.seconds)
assert all(s["codec_name"]=="aac" for s in audio)
pixels=subprocess.check_output(["ffmpeg","-v","error","-ss","1","-i",str(file),"-frames:v","1","-vf","scale=160:90,format=gray","-f","rawvideo","-"])
assert pixels and max(pixels)-min(pixels)>8,"capture is black/blank"
print(json.dumps({"file":str(file),"resolution":[video["width"],video["height"]],"fps":video["avg_frame_rate"],"duration":duration,"audio_tracks":[s.get("tags",{}).get("title") for s in audio],"bytes":int(data["format"]["size"])},indent=2))
