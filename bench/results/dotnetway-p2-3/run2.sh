#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-3
parity/bin/candidate build > $D/build2.log 2>&1; echo $? > $D/build2.status
[ "$(cat $D/build2.status)" = 0 ] || { echo 1 > $D/ALL2; exit; }
$D/replay.sh
bin/cookie-roundtrip > $D/cookie-roundtrip.log 2>&1; echo $? > $D/cookie.status
colima ssh -- bash -c "cd $PWD && bench/quick --routes post_message,sidebar --out $D/bench" > $D/bench.log 2>&1; echo $? > $D/bench.status
bin/clean-docker > /dev/null 2>&1
echo done > $D/ALL2
