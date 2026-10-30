#!/usr/bin/env bash
# Interleaved: new image (HEAD e6b2999) vs control (the image of ff1771e, src identical to base 4b7c03d; tag u42ctl), 3 reps each, alternating order.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-2
run() { # label image rep
  uptime >> $D/loads.txt
  for part in 1/2 2/2; do
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --only dotnetway:$3 --part $part --out $D/$1" >> $D/bench-$1.log 2>&1
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:u42ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
uptime >> $D/loads.txt
echo done > $D/STATUS-bench
