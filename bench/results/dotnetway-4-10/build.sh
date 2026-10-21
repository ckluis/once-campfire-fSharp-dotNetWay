#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-10
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=7fadcdefba2c3afaf0e50043bc9e367a3be45369 parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$?" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
