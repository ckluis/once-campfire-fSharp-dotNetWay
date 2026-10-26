#!/usr/bin/env bash
# Interleaved: new image (HEAD, unit 7.2) vs control campfire-dotnetway:p6ctl (309ab42), avatar,room_show,post_message, 3 reps, alternating order.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-7-2/quick
run() { # label image rep
  echo "=== $1 rep $3 $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log
  colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --routes avatar,room_show,post_message --only dotnetway:$3 --out $D/$1" >> $D/bench-$1.log 2>&1
  echo "rc=$?" >> $D/driver.log
}
N=campfire-dotnetway:app; C=campfire-dotnetway:p6ctl
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
colima ssh -- bash -c "cd $PWD && bench/quick report $D/new" >> $D/bench-new.log 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick report $D/ctl" >> $D/bench-ctl.log 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick compare $D/new" > $D/compare-new.txt 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick compare $D/ctl" > $D/compare-ctl.txt 2>&1
echo done > $D/STATUS
