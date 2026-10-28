import json,glob,sys,os,statistics as st
d=sys.argv[1]
res={}
for f in sorted(glob.glob(os.path.join(d,'*-[0-9]*.json'))):
    j=json.load(open(f))
    if 'http' not in j: continue
    for h in j['http']:
        res.setdefault((j['app'],h.get('name') or h.get('route')),[]).append(h['rps'])
for (a,r),v in sorted(res.items(), key=lambda x:(x[0][1],x[0][0])):
    print(f"{r:14s} {a:10s} median {st.median(v):9.0f}  {['%.0f'%x for x in v]}")
