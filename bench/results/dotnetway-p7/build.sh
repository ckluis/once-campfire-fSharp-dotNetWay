#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p7
H=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
# control: Phase 6 closing commit 309ab42 (Phase log), scratch worktree, tag campfire-dotnetway:p7gatectl
W=parity/out/p6ctl-wt
git worktree add --detach $W 309ab42 > $D/control-wt.log 2>&1
cp -R reference/. $W/reference/
colima ssh -- bash -c "cd $PWD/$W && CAMPFIRE_REVISION=309ab42 PARITY_CANDIDATE_APP_IMAGE=campfire-dotnetway:p7gatectl PARITY_CANDIDATE_IMAGE=campfire-candidate-p7gatectl parity/bin/candidate build" > $D/build-control.log 2>&1
echo "control rc=$?" > $D/STATUS-control
git worktree remove --force $W
bin/clean-docker > $D/clean-docker2.log 2>&1
