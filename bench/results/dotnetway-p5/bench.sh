#!/usr/bin/env bash
# Interleaved: new image (HEAD) vs control (Phase 4 verifier b2322f3, campfire-dotnetway:p4ctl), 3 reps each, alternating order.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p5/quick
run() { # label image rep
  for part in 1/2 2/2; do
    echo "=== $1 rep $3 part $part $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --only dotnetway:$3 --part $part --out $D/$1" >> $D/bench-$1.log 2>&1
    echo "rc=$?" >> $D/driver.log
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:p4ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
colima ssh -- bash -c "cd $PWD && bench/quick report $D/new" >> $D/bench-new.log 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick report $D/ctl" >> $D/bench-ctl.log 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick compare $D/new" > $D/compare-new.txt 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick compare $D/ctl" > $D/compare-ctl.txt 2>&1
echo done > $D/STATUS
