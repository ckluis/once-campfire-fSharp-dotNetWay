#!/usr/bin/env bash
# Phase 6 independent verification: network check and --fast pixel slices on the closing image (26c4ea1), one at a time.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
V=$PWD/bench/results/dotnetway-p6-verify
echo "image $(docker inspect campfire-dotnetway:app --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')" > $V/driver.log
bin/asset-check > $V/asset-check.log 2>&1; echo "asset-check rc=$?" >> $V/driver.log
bin/clean-docker > $V/clean-docker-1.log 2>&1
run() { n=$1; g=$2; fl=${3:-}
  echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $V/driver.log
  bin/pixel-parity $fl --only "$g" --out parity/out/p6v-$n > $V/$n.log 2>&1
  echo "$n rc=$?" >> $V/driver.log
  mkdir -p $V/pixels/$n
  find parity/out/p6v-$n -name report.json 2>/dev/null | while read f; do rel=${f#parity/out/p6v-$n/}; mkdir -p "$V/pixels/$n/$(dirname "$rel")"; cp "$f" "$V/pixels/$n/$rel"; done
  rm -rf parity/out/p6v-$n
  KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
run fast-interactions 'interactions/**' --fast
run fast-pwa-errors-auth 'pwa/**,errors/**,auth/**' --fast
run fast-rt-message_sent 'realtime/message_sent' --fast
run fast-rt-attachment_sent 'realtime/attachment_sent' --fast
echo done > $V/STATUS
