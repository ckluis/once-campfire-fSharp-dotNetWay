#!/usr/bin/env bash
# Phase 10 gate, part C: bench/quick, all five workloads plus the static rows, c=1 and c=16, 3 reps, new (HEAD image) against the control
# (Phase 9 gate image dcc9434, campfire-dotnetway:p10ctl), interleaved new/ctl/ctl/new/new/ctl; Rails is the stored phase7-final median (upstream-quick has the same-session Rails).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=$PWD/bench/results/dotnetway-p10; Q=$D/quick; mkdir -p $Q
R=room_show,messages_page,sidebar,search,post_message,static_css,avatar,up
log() { echo "=== $* $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/bench-driver.log; }
run() { # label image rep
  for part in 1/3 2/3 3/3; do
    log "$1 rep $3 part $part"
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --routes $R --only dotnetway:$3 --part $part --out $Q/$1" >> $Q/bench-$1.log 2>&1
    echo "$1 $3 $part rc=$?" >> $D/bench-driver.log
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:p10ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
for l in new ctl; do
  colima ssh -- bash -c "cd $PWD && bench/quick report $Q/$l" >> $Q/bench-$l.log 2>&1
  colima ssh -- bash -c "cd $PWD && bench/quick compare $Q/$l" > $Q/compare-$l.txt 2>&1
done
log done
echo done > $D/STATUS-C
