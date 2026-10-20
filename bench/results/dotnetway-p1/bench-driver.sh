#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
B=$PWD/bench/results/dotnetway-p1; D=$B/quick
for u in dotnetway:1 fsharp:1 fsharp:2 dotnetway:2 dotnetway:3 fsharp:3; do
 for p in 1/2 2/2; do
  echo "=== $u $p $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log
  colima ssh -- bash -c "cd $PWD && bench/quick --apps fsharp,dotnetway --reps 3 --only $u --part $p --out $D >> $D/run.log 2>&1" 
  echo "rc=$? $(date +%T) load $(uptime | sed 's/.*averages: //')" >> $D/driver.log
 done
done
colima ssh -- bash -c "cd $PWD && bench/quick report $D" >> $D/run.log 2>&1
colima ssh -- bash -c "cd $PWD && bench/quick compare $D" > $B/quick-compare.txt 2>&1
echo done > $D/STATUS
