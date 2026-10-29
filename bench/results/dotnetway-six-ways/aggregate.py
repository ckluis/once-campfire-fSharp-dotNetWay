#!/usr/bin/env python3
"""aggregate.py RESULTS_DIR: pool the c=16 medians over every round of every session per app and row; write table.json and table.md."""
import json, statistics, sys, glob, os
D = sys.argv[1]
APPS = [("dotnetway", "theDotNetWay", "75487b7"), ("oncrack", "onCrack", "3770dff"), ("fsharp", "F# port", "9954ad0"),
        ("rails", "Rails", "ca9b963"), ("rust", "Rust", "6dae2fd"), ("c", "C", "873a51f")]
GROUPS = [("Each route alone", "normal", "http", ["room_show", "messages_page", "sidebar", "search", "post_message"]),
          ("Reads while a writer posts 10/s", "mixed", "mixed_http", ["room_show", "messages_page", "sidebar", "search"])]
LABEL = {"room_show": "Room page", "messages_page": "Messages page", "sidebar": "Sidebar", "search": "Search", "post_message": "Post a message"}
rows = []
for gname, prefix, key, routes in GROUPS:
    sessions = sorted(d for d in os.listdir(D) if d.startswith(prefix + "-") and os.path.isdir(os.path.join(D, d)))
    for route in routes:
        vals = {}
        for app, _, _ in APPS:
            samples, sources = [], []
            for s in sessions:
                for f in sorted(glob.glob(os.path.join(D, s, app + "-*.json"))):
                    row = json.load(open(f))
                    for item in row[key]:
                        if item["route"] == route and item["conc"] == 16:
                            samples.append(item["rps"]); sources.append(os.path.relpath(f, os.path.dirname(os.path.dirname(os.path.dirname(D)))) if False else "bench/results/dotnetway-six-ways/" + s + "/" + os.path.basename(f))
            vals[app] = {"median": round(statistics.median(samples), 1), "min": min(samples), "max": max(samples), "n": len(samples), "sources": sources}
        rows.append({"group": gname, "route": LABEL[route], "key": route, "sessions": sessions, "values": vals})
out = {"apps": [{"id": a, "name": n, "commit": c} for a, n, c in APPS], "concurrency": 16, "rows": rows}
json.dump(out, open(os.path.join(D, "table.json"), "w"), indent=1)
lines = []
cur = None
for r in rows:
    if r["group"] != cur:
        cur = r["group"]; lines += ["", f"**{cur}** (requests per second, c=16, median [min-max] over {len(r['values']['rails']['sources'])} rounds)", "",
            "| route | " + " | ".join(f"{n} `{c}`" for _, n, c in APPS) + " |", "|---|" + "---:|" * len(APPS)]
    best = max(v["median"] for v in r["values"].values())
    cells = []
    for a, _, _ in APPS:
        v = r["values"][a]; t = f"{v['median']:,.0f} [{v['min']:,.0f}-{v['max']:,.0f}]"
        cells.append(f"**{t}**" if v["median"] == best else t)
    lines.append(f"| {r['route']} | " + " | ".join(cells) + " |")
lines += ["", "**theDotNetWay over Rails (this harness's Rails), pooled medians**", "", "| group | route | x Rails |", "|---|---|---:|"]
for r in rows:
    lines.append(f"| {r['group']} | {r['route']} | {r['values']['dotnetway']['median'] / r['values']['rails']['median']:.2f}x |")
open(os.path.join(D, "table.md"), "w").write("\n".join(lines) + "\n")
print("\n".join(lines))
