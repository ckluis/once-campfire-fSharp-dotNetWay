#!/usr/bin/env bash
# Phase 8 gate: HEAD vs the same-session control campfire-dotnetway:p8gatectl, 3 reps each, alternating order, in 4 parts.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p8/quick
ROUTES=room_show,messages_page,sidebar,search,post_message,avatar,static_css,up
run() { # label image rep
  for part in 1/4 2/4 3/4 4/4; do
    echo "=== $1 rep $3 part $part $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --routes $ROUTES --only dotnetway:$3 --part $part --out $D/$1" >> $D/bench-$1.log 2>&1
    echo "rc=$?" >> $D/driver.log
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:p8gatectl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
for l in new ctl; do
  colima ssh -- bash -c "cd $PWD && bench/quick report $D/$l" >> $D/bench-$l.log 2>&1
  colima ssh -- bash -c "cd $PWD && bench/quick compare $D/$l" > $D/compare-$l.txt 2>&1
done
echo done > $D/STATUS
