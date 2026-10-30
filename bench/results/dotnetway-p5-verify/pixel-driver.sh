#!/usr/bin/env bash
# Phase 5 independent verification: the phase slice --fast on every seed, then two realtime states one at a time, on the 853604f image.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p5-verify/pixels
run() { # name glob
  n=$1; g=$2
  echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $EV/driver.log
  bin/pixel-parity --fast --only "$g" --out parity/out/p5v-$n > $EV/$n.log 2>&1
  echo "$n rc=$?" >> $EV/driver.log
  mkdir -p $EV/$n
  find parity/out/p5v-$n -name report.json 2>/dev/null | while read f; do
    rel=${f#parity/out/p5v-$n/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
  rm -rf parity/out/p5v-$n
  KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
run fast "rooms/show/**,messages/**,search/**,interactions/edit_form*"
run rt-message_sent "realtime/message_sent"
run rt-message_edited "realtime/message_edited"
echo done > $EV/STATUS
