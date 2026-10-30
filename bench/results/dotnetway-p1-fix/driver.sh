#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
B=$PWD/bench/results/dotnetway-p1-fix; EV=$B/pixels; D=$B/quick
run() { n=$1; g=$2
  echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $EV/driver.log
  bin/pixel-parity --only "$g" --out parity/out/p1fix-$n > $EV/$n.log 2>&1
  echo "$n rc=$?" >> $EV/driver.log
  rm -rf parity/out/p1fix-$n; KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
run rooms "rooms/**"
run rt-unread_in_sidebar "realtime/unread_in_sidebar"
run rt-new_room_in_sidebar "realtime/new_room_in_sidebar"
run users "users/**"
echo done > $EV/STATUS
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
