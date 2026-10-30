#!/usr/bin/env bash
# Interleaved: new image (HEAD a2fa00e) vs control (built from the base 6f69c4b; tag u43ctl), 3 reps each, alternating order.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-3
run() { # label image rep
  uptime >> $D/loads.txt
  for part in 1/2 2/2; do
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --only dotnetway:$3 --part $part --out $D/$1" >> $D/bench-$1.log 2>&1
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:u43ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
uptime >> $D/loads.txt
echo done > $D/STATUS-bench
