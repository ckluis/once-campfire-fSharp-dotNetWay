#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p3
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=c2742ad01350d77dfcdd1d1f6ed85343bb8027ec parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$?" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
