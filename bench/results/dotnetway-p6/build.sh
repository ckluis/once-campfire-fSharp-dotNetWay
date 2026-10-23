#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p6
H=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
# control: Phase 5 closing commit 853604f (Phase log), scratch worktree, tag campfire-dotnetway:p5gate
W=parity/out/p5ctl-wt
git worktree add --detach $W 853604f > $D/control-wt.log 2>&1
cp -R reference/. $W/reference/
colima ssh -- bash -c "cd $PWD/$W && CAMPFIRE_REVISION=853604f PARITY_CANDIDATE_APP_IMAGE=campfire-dotnetway:p5gate PARITY_CANDIDATE_IMAGE=campfire-candidate-p5gate parity/bin/candidate build" > $D/build-control.log 2>&1
echo "control rc=$?" > $D/STATUS-control
git worktree remove --force $W
bin/clean-docker > $D/clean-docker2.log 2>&1
