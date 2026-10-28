#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=bench/results/dotnetway-p10/full/rerun; mkdir -p $EV
rr() { seed=$1; st=$2; n=$3
 for i in 1 2; do
  bin/pixel-parity --seeds $seed --only "$st" --out $PWD/parity/out/p10rr-$n-$i -- --matrix full --engines webkit > $EV/$n-$i.log 2>&1; echo "$n $i rc=$?"; grep -hE "^seed|DIFF" $EV/$n-$i.log | cut -c1-200
  find parity/out/p10rr-$n-$i -name report.json | while read f; do rel=${f#parity/out/p10rr-$n-$i/}; mkdir -p "$EV/$n-$i/$(dirname "$rel")"; cp "$f" "$EV/$n-$i/$rel"; done; rm -rf parity/out/p10rr-$n-$i
 done; }
rr custom_styles search/custom_styles cs
rr default search/results/many many
KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
