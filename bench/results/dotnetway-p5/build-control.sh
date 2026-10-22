#!/usr/bin/env bash
# Control: Phase 3 verifier b2322f3 in a scratch worktree (submodule contents copied in), tagged campfire-dotnetway:p4ctl.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p5
W=parity/out/p4ctl-wt
git worktree add --detach $W b2322f3 > $D/control-wt.log 2>&1
cp -R reference/. $W/reference/
colima ssh -- bash -c "cd $PWD/$W && CAMPFIRE_REVISION=b2322f3 PARITY_CANDIDATE_APP_IMAGE=campfire-dotnetway:p4ctl PARITY_CANDIDATE_IMAGE=campfire-candidate-p4ctl parity/bin/candidate build" > $D/build-control.log 2>&1
echo "control rc=$?" > $D/STATUS-control
git worktree remove --force $W
bin/clean-docker > $D/clean-docker2.log 2>&1
