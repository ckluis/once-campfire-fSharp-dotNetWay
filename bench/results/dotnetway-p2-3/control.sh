#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-3
colima ssh -- bash -c "cd $PWD && bin/publish-mounted /var/tmp/mounted-new" > $D/pub-new.log 2>&1
git checkout 6476beb -- src
colima ssh -- bash -c "cd $PWD && bin/publish-mounted /var/tmp/mounted-ctl" > $D/pub-ctl.log 2>&1
git checkout HEAD -- src
git status --short > $D/after-checkout.txt
for pair in 1 2; do
  colima ssh -- bash -c "cd $PWD && bench/quick --routes post_message,sidebar --mounted /var/tmp/mounted-new --reps 2 --out $D/pair$pair-new" > $D/pair$pair-new.log 2>&1
  colima ssh -- bash -c "cd $PWD && bench/quick --routes post_message,sidebar --mounted /var/tmp/mounted-ctl --reps 2 --out $D/pair$pair-ctl" > $D/pair$pair-ctl.log 2>&1
done
echo done > $D/ALL3
