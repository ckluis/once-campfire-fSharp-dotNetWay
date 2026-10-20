#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
B=/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2-0
colima ssh -- bash -c "cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway && bin/publish-mounted /var/tmp/campfire-mounted-p2-0" > $B/publish.log 2>&1
echo "publish rc=$?" >> $B/driver.log
for round in 1 2; do
 colima ssh -- bash -c "cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway && bench/quick --reps 3 --routes post_message --out $B/old >> $B/run-old.log 2>&1"
 echo "old rc=$? $(date +%T)" >> $B/driver.log
 colima ssh -- bash -c "cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway && bench/quick --reps 3 --routes post_message --mounted /var/tmp/campfire-mounted-p2-0 --out $B/new >> $B/run-new.log 2>&1"
 echo "new rc=$? $(date +%T)" >> $B/driver.log
 break
done
echo done > $B/STATUS
