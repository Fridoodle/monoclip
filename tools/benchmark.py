import subprocess,psutil,time,json,pathlib,statistics
root=pathlib.Path(__file__).resolve().parents[1]
exe=root/"tests/MonoClip.NativeTests/bin/Release/net10.0-windows/MonoClip.NativeTests.exe"
log=root/"verification/benchmark-native.log";log.parent.mkdir(exist_ok=True)
samples=[]
with log.open("w",encoding="utf-8") as output:
    child=subprocess.Popen([str(exe),"--benchmark"],stdout=output,stderr=subprocess.STDOUT)
    p=psutil.Process(child.pid);start=time.monotonic();p.cpu_percent(None)
    while child.poll() is None:
        try:
            cpu=p.cpu_percent(interval=.5);elapsed=time.monotonic()-start
            mem=p.memory_info();io=p.io_counters()
            samples.append({"elapsed":elapsed,"cpu_one_core_percent":cpu,"cpu_machine_percent":cpu/psutil.cpu_count(),"working_set_mb":mem.rss/1024**2,"private_mb":getattr(mem,"private",mem.vms)/1024**2,"write_bytes":io.write_bytes,"threads":p.num_threads()})
        except psutil.NoSuchProcess:break
        if time.monotonic()-start>65:child.kill();raise SystemExit("benchmark timed out")
    code=child.wait()
steady=[s for s in samples if 10<s["elapsed"]<35]
assert code==0,log.read_text()
result={"mode":"1080p60 desktop capture, AMD H264, two audio sources, 30-second replay; native engine harness", "logical_cpus":psutil.cpu_count(),"sample_count":len(steady),"cpu_machine_mean_percent":statistics.mean(s["cpu_machine_percent"] for s in steady),"cpu_one_core_mean_percent":statistics.mean(s["cpu_one_core_percent"] for s in steady),"working_set_max_mb":max(s["working_set_mb"] for s in steady),"private_max_mb":max(s["private_mb"] for s in steady),"steady_write_delta_bytes":steady[-1]["write_bytes"]-steady[0]["write_bytes"],"samples":samples,"exit_code":code}
(root/"verification/benchmark-native.json").write_text(json.dumps(result,indent=2))
print(json.dumps({k:v for k,v in result.items() if k!="samples"},indent=2))
