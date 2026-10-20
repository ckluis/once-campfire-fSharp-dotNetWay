#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-10
echo running > $D/REPLAY
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build" > $D/build.log 2>&1
echo $? > $D/build.status
sed -i '' 's/^echo done > .*REPLAY$/echo done-replay > $D\/REPLAY/' $D/replay.sh
$D/replay.sh
