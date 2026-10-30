#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-3
rev=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$rev parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$?" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
