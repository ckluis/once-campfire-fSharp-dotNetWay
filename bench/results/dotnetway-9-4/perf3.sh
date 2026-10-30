#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-9-4
FSHARP_IMAGE=campfire-dotnetway:app bench/breakdown perf --apps fsharp --conc 16 --routes post_message --out $D/perf-app-c16 > $D/perf-app-c16.log 2>&1
echo "rc=$?" > $D/STATUS-perf3
