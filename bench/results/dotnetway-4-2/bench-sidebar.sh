#!/usr/bin/env bash
# Second bench, sidebar only (the one workload the fix of 09b424e touches), HEAD 09b424e (new2) vs the same control image, interleaved.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-2
run() { # label image rep
  uptime >> $D/loads2.txt
  colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --routes sidebar --only dotnetway:$3 --out $D/$1" >> $D/bench-$1.log 2>&1
}
N=campfire-dotnetway:app; C=campfire-dotnetway:u42ctl
run new2 $N 1; run ctl2 $C 1
run ctl2 $C 2; run new2 $N 2
run new2 $N 3; run ctl2 $C 3
uptime >> $D/loads2.txt
echo done > $D/STATUS-bench2
