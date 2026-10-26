#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-7-1
H=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
# control: Phase 6 close 309ab42, scratch worktree, tag campfire-dotnetway:p6ctl
W=parity/out/p6ctl-wt
git worktree add --detach $W 309ab42 > $D/control-wt.log 2>&1
cp -R reference/. $W/reference/
colima ssh -- bash -c "cd $PWD/$W && CAMPFIRE_REVISION=309ab42 PARITY_CANDIDATE_APP_IMAGE=campfire-dotnetway:p6ctl PARITY_CANDIDATE_IMAGE=campfire-candidate-p6ctl parity/bin/candidate build" > $D/build-control.log 2>&1
echo "control rc=$?" > $D/STATUS-control
git worktree remove --force $W
bin/clean-docker > $D/clean-docker2.log 2>&1
echo done > $D/STATUS-builds
