#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
colima ssh -- bash -c "cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway && bench/upstream-quick --commit HEAD --rounds 3 --out /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p10/uq-normal" > /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p10/uq-normal.stdout 2>&1
colima ssh -- bash -c "cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway && bench/upstream-quick --commit HEAD --rounds 3 --mixed --out /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p10/uq-mixed" > /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p10/uq-mixed.stdout 2>&1
echo done > /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p10/STATUS-U
