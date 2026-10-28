import json,glob,statistics as st,sys
for q in ('quick-94a','quick-94b'):
    print(q)
    for app in ('fsharp','dotnetway'):
        for c in (1,16):
            rps=[];cpu=[];host=[]
            for f in sorted(glob.glob(f'bench/results/{q}/{app}-?.json')):
                d=json.load(open(f))
                for h in d['http']:
                    if h['conc']==c and h['route']=='post_message':
                        rps.append(h['rps']); cpu.append(h['cost']['server_cpu_us_per_req'])
            print(f"  {app:9} c={c:2} rps med {st.median(rps):8.1f} [{min(rps):.0f}-{max(rps):.0f}]  cpu/req med {st.median(cpu):7.1f} [{min(cpu):.0f}-{max(cpu):.0f}] n={len(rps)}")
