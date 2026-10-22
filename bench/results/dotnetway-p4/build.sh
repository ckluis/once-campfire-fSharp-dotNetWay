#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p4
H=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
# control: Phase 3 verifier b632a7c in a scratch worktree, tag campfire-dotnetway:p3ctl
W=parity/out/p3ctl-wt
git worktree add --detach $W b632a7c > $D/control-wt.log 2>&1
colima ssh -- bash -c "cd $PWD/$W && CAMPFIRE_REVISION=b632a7c PARITY_CANDIDATE_APP_IMAGE=campfire-dotnetway:p3ctl PARITY_CANDIDATE_IMAGE=campfire-candidate-p3ctl parity/bin/candidate build" > $D/build-control.log 2>&1
echo "control rc=$?" > $D/STATUS-control
git worktree remove --force $W
bin/clean-docker > $D/clean-docker2.log 2>&1
