#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p5
H=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H" > $D/STATUS-build
bin/clean-docker > $D/clean-docker.log 2>&1
# control: Phase 4 verifier b2322f3 in a scratch worktree, tag campfire-dotnetway:p4ctl
W=parity/out/p4ctl-wt
git worktree add --detach $W b2322f3 > $D/control-wt.log 2>&1
colima ssh -- bash -c "cd $PWD/$W && CAMPFIRE_REVISION=b2322f3 PARITY_CANDIDATE_APP_IMAGE=campfire-dotnetway:p4ctl PARITY_CANDIDATE_IMAGE=campfire-candidate-p4ctl parity/bin/candidate build" > $D/build-control.log 2>&1
echo "control rc=$?" > $D/STATUS-control
git worktree remove --force $W
bin/clean-docker > $D/clean-docker2.log 2>&1
