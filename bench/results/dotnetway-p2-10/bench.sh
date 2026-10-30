#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-10
colima ssh -- bash -c "cd $PWD && bench/quick --out $D/bench" > $D/bench.log 2>&1; echo $? > $D/bench.status
colima ssh -- bash -c "cd $PWD && bench/quick --apps fsharp --reps 1 --routes sidebar,post_message --out $D/control" > $D/control.log 2>&1; echo $? > $D/control.status
bin/clean-docker > /dev/null 2>&1
echo done > $D/BENCH
