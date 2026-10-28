#!/usr/bin/env python3
"""bench/upstream-quick's summary: read the harness's output in OUT/raw and OUT/host.txt, write OUT/summary.json and OUT/summary.md.

  upstream_quick.py OUT [--mounted]

Per route and app: median, min and max requests per second over the rounds, and x Rails (median over median, same session).
Also the commits and dirty flags the harness recorded, the image ids and revision labels, the seed sha, and the host probe and load
before and after. --mounted marks theDotNetWay's dirty flag "mounted" (the layered build is not the clean clone's commit).
"""
import json
import os
import statistics
import sys

ORDER = ["room_show", "messages_page", "sidebar", "search", "post_message"]


def main():
    out = sys.argv[1]
    mounted = "--mounted" in sys.argv
    raw = os.path.join(out, "raw")
    mixed = os.path.exists(os.path.join(raw, "mixed-summary.json"))
    src = json.load(open(os.path.join(raw, "mixed-summary.json" if mixed else "summary.json")))
    meta = src["metadata"]
    results = src.get("mixed_results") or src["results"]
    apps = list(results)
    table = {}
    for route in ORDER:
        if not any(route in results[a] for a in apps):
            continue
        row = {}
        for a in apps:
            runs = results[a].get(route, {}).get("runs")
            if runs:
                row[a] = {"median": statistics.median(runs), "min": min(runs), "max": max(runs), "runs": runs}
        rails = row.get("rails", {}).get("median")
        for a, v in row.items():
            v["x_rails"] = round(v["median"] / rails, 2) if rails else None
        table[route] = row
    revs = {a: dict(r) for a, r in meta.get("source_revisions", {}).items()}
    if mounted and "dotnetway" in revs:
        revs["dotnetway"]["dirty"] = "mounted"
    host = open(os.path.join(out, "host.txt")).read().strip().splitlines() if os.path.exists(os.path.join(out, "host.txt")) else []
    summary = {
        "profile": meta.get("profile"), "rounds": meta.get("rounds"), "routes": meta.get("routes"),
        "seed_sha256": meta.get("seed_sha256"), "verification_revision": meta.get("verification_revision"),
        "source_revisions": revs, "images": meta.get("images"),
        "image_labels": {a: l.get("org.opencontainers.image.revision") for a, l in meta.get("image_labels", {}).items()},
        "host": host, "table": table,
    }
    json.dump(summary, open(os.path.join(out, "summary.json"), "w"), indent=2)
    lines = [f"# upstream-quick ({'mixed' if mixed else 'normal'} profile, {meta.get('rounds')} rounds, c=16)", "",
             "Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.", ""]
    cols = [a for a in ["rails", "fsharp", "dotnetway"] if a in apps] + [a for a in apps if a not in ("rails", "fsharp", "dotnetway")]
    lines.append("| route | " + " | ".join(cols) + " |")
    lines.append("|---|" + "---|" * len(cols))
    for route, row in table.items():
        cells = []
        for a in cols:
            v = row.get(a)
            if not v:
                cells.append("-")
            elif a == "rails":
                cells.append(f"{v['median']:,.0f} ({v['min']:,.0f}-{v['max']:,.0f})")
            else:
                cells.append(f"{v['median']:,.0f} ({v['min']:,.0f}-{v['max']:,.0f}) **{v['x_rails']}x**")
        lines.append(f"| {route} | " + " | ".join(cells) + " |")
    lines += ["", "Commits:"]
    for a, r in revs.items():
        lines.append(f"- {a}: `{r.get('head', '')[:7]}` dirty={str(r.get('dirty')).lower()}  image `{(meta.get('images') or {}).get(a, '')[:19]}`")
    lines += [f"- seed sha256 `{(meta.get('seed_sha256') or '')[:8]}`, harness `{(meta.get('verification_revision') or '')[:7]}`", "", "Host:"]
    lines += [f"- {h}" for h in host]
    text = "\n".join(lines) + "\n"
    open(os.path.join(out, "summary.md"), "w").write(text)
    print(text)


main()
