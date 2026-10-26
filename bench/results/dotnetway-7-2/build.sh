#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-7-2
H=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
echo done >> $D/STATUS-build
