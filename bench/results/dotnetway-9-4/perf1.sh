#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-9-4
for t in p8ctl p9pre app; do
  FSHARP_IMAGE=campfire-dotnetway:$t bench/breakdown perf --apps fsharp --routes post_message --conc 1 --out $D/perf-$t > $D/perf-$t.log 2>&1
  echo "$t rc=$?" >> $D/STATUS-perf
done
echo done >> $D/STATUS-perf2
