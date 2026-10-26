#!/usr/bin/env python3
"""theDotNetWay results against the stored Rust and F# port baselines, and the Phase 7 progress log.

  quicklog.py compare DIR                 table of the dotnetway runs in DIR (or the port's fsharp runs, when there are none) against
                                          bench/results/phase7-final (stored Rails, Rust and F# port medians, one session), with the
                                          validity checks
  quicklog.py log DIR --unit U --tier N --change "..." --kept true|false [--build mounted|DIGEST] [--commit SHA]
                                          append one line to bench/results/phase7-log.jsonl (raw F# numbers per workload), then rebuild
                                          index.html (the climb chart) with bench/progress/build.py

Rust and the port are never measured here: their numbers are the medians of bench/results/phase7-final/{rust,fsharp}-*.json (the same\nwarm-up and harness). Every ratio this prints is "vs stored baseline".
"""
import argparse
import glob
import json
import os
import statistics
import subprocess
import sys
from datetime import datetime, timezone

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
START = os.path.join(ROOT, "bench/results/phase7-start")
FINAL = os.path.join(ROOT, "bench/results/phase7-final")   # Rails, Rust and the port, measured in one session (Phase 0 of theDotNetWay compares against it)
LOG = os.path.join(ROOT, "bench/results/phase7-log.jsonl")
DOTNETWAY_LOG = os.path.join(ROOT, "bench/results/dotnetway-log.jsonl")
WORKLOADS = ["room_show", "messages_page", "sidebar", "search", "post_message"]
LABEL = {"room_show": "room page", "messages_page": "messages page", "sidebar": "sidebar", "search": "search", "post_message": "post a message"}


def load(directory, app):
    runs = []
    for p in sorted(glob.glob(os.path.join(directory, f"{app}-[0-9].json"))):
        with open(p) as f:
            runs.append(json.load(f))
    return runs


def cells(runs):
    """{(route, conc): {"rps": [..], "cpu": [..], "runs": [..]}} over the gzip runs of every rep."""
    out = {}
    for r in runs:
        for h in r["http"]:
            if not h.get("gzip"):
                continue
            c = out.setdefault((h["route"], h["conc"]), {"rps": [], "cpu": [], "runs": []})
            c["rps"].append(h["rps"])
            cpu = h.get("cost", {}).get("server_cpu_us_per_req")
            if cpu is not None:
                c["cpu"].append(cpu)
            c["runs"].append(h)
    return out


def med(v):
    return statistics.median(v) if v else None


def spread(v):
    return "" if len(v) < 2 else f" [{min(v):,.0f}-{max(v):,.0f}]"


def fmt(x, d=0):
    return "-" if x is None else f"{x:,.{d}f}"


def compare(directory):
    app = "dotnetway" if load(directory, "dotnetway") else "fsharp"
    name = "theDotNetWay" if app == "dotnetway" else "F# port"
    mine = cells(load(directory, app))
    if not mine:
        sys.exit(f"no {app}-N.json in {directory}")
    rust = cells(load(FINAL, "rust"))
    port = cells(load(FINAL, "fsharp"))
    rails = cells(load(FINAL, "reference"))
    print(f"{name} in {directory} against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)\n")
    print(f"| Workload | c | {name} req/s | F# port stored | vs port | Rust stored | **{name} / Rust (vs stored Rust baseline)** | Rails stored | **{name} / Rails** | CPU us/req | port | Rust stored |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for w in WORKLOADS:
        for c in (16, 1):
            m = mine.get((w, c))
            if not m:
                continue
            r, s, a = rust.get((w, c)), port.get((w, c)), rails.get((w, c))
            mr, rr, sr, ar = med(m["rps"]), med(r["rps"]) if r else None, med(s["rps"]) if s else None, med(a["rps"]) if a else None
            vs = lambda x: f"{mr / x:.3f}x" if x else "-"
            print(f"| {LABEL[w]} | {c} | {fmt(mr)}{spread(m['rps'])} | {fmt(sr)} | {vs(sr)} | {fmt(rr)} | **{vs(rr)}** | {fmt(ar)} | **{vs(ar)}** | "
                  f"{fmt(med(m['cpu']), 1)}{spread(m['cpu']) if len(m['cpu']) > 1 else ''} | {fmt(med(s['cpu']), 1) if s else '-'} | {fmt(med(r['cpu']), 1) if r else '-'} |")
    # validity: statuses, errors, response bytes, rows written, warm-up, host noise
    print("\nChecks:")
    bad = 0
    for (w, c), m in sorted(mine.items()):
        for h in m["runs"]:
            non2xx = {k: v for k, v in h["statuses"].items() if not k.startswith("2")}
            if non2xx or h["errors"]:
                bad += 1
                print(f"  FAIL {w} c={c}: statuses {h['statuses']} errors {h['errors']}")
            if not h.get("warmup", {}).get("settled", True):
                print(f"  note {w} c={c}: warm-up did not settle ({h['warmup'].get('reason')})")
            ratio = h.get("host_probe_after", {}).get("ratio")
            if ratio and ratio > 1.10:
                print(f"  note {w} c={c}: host probe {ratio} after the window (noisy host)")
    print(f"  responses: every one 2xx with 0 connection errors in {sum(len(m['runs']) for m in mine.values())} runs" if not bad else f"  {bad} run(s) with errors")
    # Rust's decoded size per route, except where the Falco.Markup pages differ on purpose (unit 4.11): those routes have a stored expected
    # size (bench/lib/expected-sizes.json, the size after the views moved to Falco.Markup; update it when a template changes on purpose; refreshed in Phase 7 unit 7.1 for Phase 6's fingerprinted `/assets/` URLs, 2 bytes longer each).
    rust_probe = {p["route"]: p["gzip"]["decoded_bytes"] for p in (load(FINAL, "rust") or [{"probe": []}])[0]["probe"]}
    with open(os.path.join(ROOT, "bench/lib/expected-sizes.json")) as f:
        rust_probe.update(json.load(f))
    for r in load(directory, app):
        for p in r["probe"]:
            want = rust_probe.get(p["route"])
            if want is not None and p["gzip"]["decoded_bytes"] != want:
                print(f"  FAIL decoded bytes {p['route']}: {p['gzip']['decoded_bytes']} vs expected {want}")
                bad += 1
        for row in r["post_rows"]:
            if row["rows"] != sum(v for k, v in row["statuses"].items() if int(k) < 400):
                print(f"  FAIL post rows {row}")
                bad += 1
    if not bad:
        print("  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase")
    return bad


def source_commit(build):
    """The commit the measured build was made from, not whatever HEAD is when the run is logged (other commits,
    such as the page's, land in between). Mounted builds: the REVISION file bin/publish-mounted writes. Image builds:
    campfire-dotnetway:app's org.opencontainers.image.revision label (SOURCE_REVISION). Otherwise HEAD."""
    def run(*cmd):
        try:
            return subprocess.run(cmd, capture_output=True, text=True, timeout=20).stdout.strip()
        except (OSError, subprocess.SubprocessError):
            return ""
    if build == "mounted":
        for d in (os.environ.get("MOUNTED_DIR", "/var/tmp/campfire-mounted"),):
            path = os.path.join(d, "REVISION")
            if os.path.exists(path):
                with open(path) as f:
                    rev = f.read().strip()
                if rev:
                    return rev[:7]
    else:
        # Not GIT_REVISION: that is the reference's pinned commit, which the app echoes for parity.
        rev = run("docker", "image", "inspect", "-f", '{{index .Config.Labels "org.opencontainers.image.revision"}}', "campfire-dotnetway:app")
        if rev and rev != "<no value>":
            return rev[:7]
    return run("git", "-C", ROOT, "rev-parse", "--short", "HEAD")


def log(args):
    app = "dotnetway" if load(args.dir, "dotnetway") else "fsharp"
    mine = cells(load(args.dir, app))
    workloads = {}
    for (w, c), m in mine.items():
        workloads.setdefault(w, {})[f"c{c}"] = {"rps": round(med(m["rps"]), 1), "cpu_us": round(med(m["cpu"]), 1) if m["cpu"] else None, "reps": len(m["rps"])}
    commit = args.commit or source_commit(args.build)
    entry = {"time": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "unit": args.unit, "tier": args.tier, "change": args.change,
             "commit": commit, "build": args.build, "kept": args.kept == "true", "workloads": workloads}
    # theDotNetWay keeps its own log (the port's phase7-log.jsonl is history, and the climb chart reads it).
    path = DOTNETWAY_LOG if app == "dotnetway" else LOG
    with open(path, "a") as f:
        f.write(json.dumps(entry) + "\n")
    print("logged:", json.dumps(entry))
    if app != "dotnetway":
        subprocess.run([sys.executable, os.path.join(ROOT, "bench/progress/build.py")], check=True)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("compare"); c.add_argument("dir")
    l = sub.add_parser("log"); l.add_argument("dir"); l.add_argument("--unit", required=True, choices=["database", "runtime", "crypto-logging", "post", "final", "threadpool"])
    l.add_argument("--tier", type=int, required=True, choices=[2, 3]); l.add_argument("--change", required=True)
    l.add_argument("--kept", required=True, choices=["true", "false"]); l.add_argument("--build", default="mounted"); l.add_argument("--commit")
    a = ap.parse_args()
    if a.cmd == "compare":
        sys.exit(1 if compare(a.dir) else 0)
    log(a)
