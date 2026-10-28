#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-9-4
FSHARP_IMAGE=campfire-dotnetway:p8ctl bench/quick --reps 5 --apps fsharp,dotnetway --routes post_message --out bench/results/quick-94a > $D/quickA.log 2>&1
echo "rc=$?" > $D/STATUS-quickA
FSHARP_IMAGE=campfire-dotnetway:p9ctl bench/quick --reps 5 --apps fsharp,dotnetway --routes post_message --out bench/results/quick-94b > $D/quickB.log 2>&1
echo "rc=$?" > $D/STATUS-quickB
