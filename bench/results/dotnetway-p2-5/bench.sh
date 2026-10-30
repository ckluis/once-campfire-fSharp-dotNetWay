#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-5
colima ssh -- bash -c "cd $PWD && bench/quick --out $D/bench" > $D/bench.log 2>&1; echo $? > $D/bench.status
bin/clean-docker > /dev/null 2>&1
echo done > $D/BENCH
