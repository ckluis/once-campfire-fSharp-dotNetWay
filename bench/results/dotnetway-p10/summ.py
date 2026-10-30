#!/usr/bin/env python3
"""Phase 10 gate: medians per (route, c) of the quick/{new,ctl} runs, host probe, floor check against the stored (phase7-final) and the same-session Rails."""
import json, glob, statistics as st, sys, os
base = os.path.dirname(os.path.abspath(__file__)) + "/quick/"
def load(d):
    rows = {}
    probes, errs, bad, n = [], 0, 0, 0
    for f in sorted(glob.glob(base + d + "/*-[0-9].json")):
        j = json.load(open(f))
        for h in j["http"]:
            k = (h["route"], h["conc"])
            rows.setdefault(k, []).append((h["rps"], h["cost"]["server_cpu_us_per_req"]))
            probes.append(h["host_probe_after"]["ratio"]); n += 1
            errs += h.get("errors", 0)
            bad += sum(v for s, v in h["statuses"].items() if not s.startswith("2"))
    return rows, probes, errs, bad, n
new, pn, en, bn, nn = load("new"); ctl, pc, ec, bc, nc = load("ctl")
stored = {("room_show",16):239,("room_show",1):107,("messages_page",16):381,("messages_page",1):218,("sidebar",16):599,("sidebar",1):287,("search",16):408,("search",1):193,("post_message",16):330,("post_message",1):167,("static_css",16):98776,("static_css",1):15167,("avatar",16):69481,("avatar",1):12838,("up",16):4229,("up",1):2101}
med = lambda xs: st.median(xs)
routes = ["room_show","messages_page","sidebar","search","post_message","static_css","avatar","up"]
print("| route | c | new req/s [range] | ctl req/s | new vs ctl | Rails (stored) | x Rails (stored) | CPU us/req new / ctl |")
print("|---|---:|---|---:|---:|---:|---:|---|")
for r in routes:
    for c in (16,1):
        a = new[(r,c)]; b = ctl[(r,c)]
        an = med([v[0] for v in a]); bn_ = med([v[0] for v in b])
        s = stored.get((r,c))
        print(f"| {r} | {c} | {an:,.0f} [{min(v[0] for v in a):,.0f}-{max(v[0] for v in a):,.0f}] | {bn_:,.0f} | {100*(an/bn_-1):+.1f}% | {s} | {an/s:.2f}x | {med([v[1] for v in a]):,.0f} / {med([v[1] for v in b]):,.0f} |")
for nm,p,e,b,n in (("new",pn,en,bn,nn),("ctl",pc,ec,bc,nc)):
    print(f"{nm}: {n} runs, host probe {min(p):.3f}-{max(p):.3f}, {sum(1 for x in p if x>1.10)} above 1.10, conn errors {e}, non-2xx {b}")
