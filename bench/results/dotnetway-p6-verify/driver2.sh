#!/usr/bin/env bash
# Phase 6 independent verification, resumed session: one lean (no --fast: three browsers, both themes, breakpoints) slice on the closing image (26c4ea1).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
V=$PWD/bench/results/dotnetway-p6-verify
rm -f $V/STATUS2
echo "image $(docker inspect campfire-dotnetway:app --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')" > $V/driver2.log
n=lean-rooms-auth; g='rooms/show/designers,auth/sign_in*'
echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $V/driver2.log
bin/pixel-parity --seeds default --only "$g" --out parity/out/p6v-$n > $V/$n.log 2>&1
echo "$n rc=$?" >> $V/driver2.log
mkdir -p $V/pixels/$n
find parity/out/p6v-$n -name report.json 2>/dev/null | while read f; do rel=${f#parity/out/p6v-$n/}; mkdir -p "$V/pixels/$n/$(dirname "$rel")"; cp "$f" "$V/pixels/$n/$rel"; done
rm -rf parity/out/p6v-$n
KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
echo "=== end $(date +%T) df $(df -h / | tail -1 | awk '{print $4}')" >> $V/driver2.log
echo done > $V/STATUS2
