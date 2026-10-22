#!/usr/bin/env bash
# Phase 4 verifier: the floor's thinnest cells (post a message, sidebar; c=1 and c=16), 3 reps interleaved, new image (49e1e09) vs
# the 4.10 control (campfire-dotnetway:p411ctl, already built), same session.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p4-verify/quick
run() { # label image rep
  echo "=== $1 rep $3 $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log
  colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --only dotnetway:$3 --routes post_message,sidebar --out $D/$1" >> $D/bench-$1.log 2>&1
  echo "rc=$?" >> $D/driver.log
}
N=campfire-dotnetway:app; C=campfire-dotnetway:p411ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
colima ssh -- bash -c "cd $PWD && bench/quick report $D/new" >> $D/bench-new.log 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick report $D/ctl" >> $D/bench-ctl.log 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick compare $D/new" > $D/compare-new.txt 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick compare $D/ctl" > $D/compare-ctl.txt 2>&1
echo done > $D/STATUS
