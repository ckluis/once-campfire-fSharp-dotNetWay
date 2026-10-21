#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-11
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=67430ab94cd6a8a5c3245509c990d998dbcdfec4 parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$?" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
