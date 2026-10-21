#!/usr/bin/env bash
# New image only (HEAD 43df8fb), 3 reps, the whole matrix in two parts; compared with 3.4's recorded medians (cross-session).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-3-5
for part in 1/2 2/2; do
  colima ssh -- bash -c "cd $PWD && bench/quick --reps 3 --only dotnetway:1 --part $part --out $D/new" >> $D/bench-new.log 2>&1
done
echo done > $D/STATUS-bench
