#!/usr/bin/env bash
# FULL pixel matrix once: every seed, all engines, viewports, both schemes, breakpoint sweep.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p4/full
bin/pixel-parity --out $PWD/parity/out/p4full -- --matrix full > $EV/full.log 2>&1
echo "rc=$?" > $EV/rc
find parity/out/p4full -name report.json -o -name '*.log' 2>/dev/null | while read f; do rel=${f#parity/out/p4full/}; mkdir -p "$EV/$(dirname "$rel")"; cp "$f" "$EV/$rel"; done
echo done > $EV/STATUS
