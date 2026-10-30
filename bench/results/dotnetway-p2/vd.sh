#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2
CAMPFIRE_REQUIRE_SEED=1 bin/verify > $D/verify.log 2>&1; echo $? > $D/verify.status
bin/db-differential > $D/db-differential.log 2>&1; echo $? > $D/db-differential.status
bin/kloc > $D/kloc.md 2>&1; echo $? > $D/kloc.status
echo done > $D/VD
