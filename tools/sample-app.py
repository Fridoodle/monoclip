import psutil,time,json,pathlib,sys,statistics
name=sys.argv[1] if len(sys.argv)>1 else "MonoClip.exe"
p=next(p for p in psutil.process_iter(["name"]) if p.info["name"]==name)
p.cpu_percent(None);samples=[]
for i in range(16):
    cpu=p.cpu_percent(interval=.5);m=p.memory_info();samples.append({"cpu_machine_percent":cpu/psutil.cpu_count(),"working_set_mb":m.rss/1024**2,"private_mb":getattr(m,"private",m.vms)/1024**2})
r={"pid":p.pid,"name":name,"cpu_machine_mean_percent":statistics.mean(x["cpu_machine_percent"] for x in samples),"working_set_max_mb":max(x["working_set_mb"] for x in samples),"private_max_mb":max(x["private_mb"] for x in samples)}
print(json.dumps(r,indent=2))
