#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-3
parity/bin/candidate build > $D/build.log 2>&1; echo $? > $D/build.status
[ "$(cat $D/build.status)" = 0 ] || { echo 1 > $D/ALL; exit; }
bin/pixel-parity --fast --only 'bot_api/**,auth/**,errors/**' --out parity/out/p2-3 > $D/pixel.log 2>&1; echo $? > $D/pixel.status
bin/clean-parity > /dev/null 2>&1
echo done > $D/ALL
