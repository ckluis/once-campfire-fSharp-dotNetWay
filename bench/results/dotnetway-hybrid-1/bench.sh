#!/usr/bin/env bash
# Interleaved: new image (HEAD) vs control (h1-control 0cf9739, campfire-dotnetway:h1ctl), 3 reps each, alternating order.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-hybrid-1
run() { # label image rep
  for part in 1/2 2/2; do
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --only dotnetway:$3 --part $part --out $D/$1" >> $D/bench-$1.log 2>&1
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:h1ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
echo done > $D/STATUS-bench
