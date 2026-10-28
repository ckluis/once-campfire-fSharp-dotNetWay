#!/usr/bin/env bash
# the originally differing cells, alone: webkit, dark, the viewport of the cell, three runs each
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=bench/results/dotnetway-p9/full/rerun; mkdir -p $EV
rr() { seed=$1; st=$2; n=$3; vp=$4
 for i in 1 2 3; do
  bin/pixel-parity --seeds $seed --only "$st" --out $PWD/parity/out/p9rs-$n-$i -- --matrix full --engines webkit --schemes dark --viewports $vp > $EV/cell-$n-$i.log 2>&1; echo "$n $vp $i rc=$?"; grep -hE "^seed|DIFF" $EV/cell-$n-$i.log | cut -c1-160
  rm -rf parity/out/p9rs-$n-$i
 done; }
rr custom_styles search/custom_styles cs tablet
rr default search/results/many many tablet
rr default search/results/many many phone
KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
