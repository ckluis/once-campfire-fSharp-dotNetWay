#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-10
echo running > $D/GATES
bin/pixel-parity --fast --only 'account/**,users/**,autocompletable/**' > $D/pixel.log 2>&1; echo $? > $D/pixel.status
bin/clean-parity > /dev/null 2>&1
bin/cookie-roundtrip > $D/cookie-roundtrip.log 2>&1; echo $? > $D/cookie.status
echo done > $D/GATES
