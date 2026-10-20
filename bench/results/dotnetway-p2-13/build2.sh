#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-13
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build" > $D/build-final.log 2>&1
echo $? > $D/build-final.status
