#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-9-4
FSHARP_IMAGE=campfire-dotnetway:app bench/breakdown perf --apps fsharp --conc 1 --out $D/perf-all-c1 > $D/perf-all-c1.log 2>&1
echo "rc=$?" > $D/STATUS-perf-all
