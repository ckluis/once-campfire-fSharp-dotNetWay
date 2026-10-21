#!/usr/bin/env bash
# Interleaved: new image (HEAD HEAD) vs control (built from the base 18aad84; tag u46ctl), 3 reps each, alternating order.
# Only the workloads the unit touches: the sidebar and search, c=1 and c=16.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-6
run() { # label image rep
  uptime >> $D/loads.txt
  for part in 1/2 2/2; do
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --routes sidebar,search --only dotnetway:$3 --part $part --out $D/$1" >> $D/bench-$1.log 2>&1
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:u46ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
uptime >> $D/loads.txt
echo done > $D/STATUS-bench
