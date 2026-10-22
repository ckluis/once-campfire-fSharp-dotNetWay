#!/usr/bin/env bash
# Control: Phase 3 verifier b632a7c in a scratch worktree (submodule contents copied in), tagged campfire-dotnetway:p3ctl.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p4
W=parity/out/p3ctl-wt
git worktree add --detach $W b632a7c > $D/control-wt.log 2>&1
cp -R reference/. $W/reference/
colima ssh -- bash -c "cd $PWD/$W && CAMPFIRE_REVISION=b632a7c PARITY_CANDIDATE_APP_IMAGE=campfire-dotnetway:p3ctl PARITY_CANDIDATE_IMAGE=campfire-candidate-p3ctl parity/bin/candidate build" > $D/build-control.log 2>&1
echo "control rc=$?" > $D/STATUS-control
git worktree remove --force $W
bin/clean-docker > $D/clean-docker2.log 2>&1
