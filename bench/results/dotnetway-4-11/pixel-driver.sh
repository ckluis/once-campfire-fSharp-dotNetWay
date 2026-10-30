#!/usr/bin/env bash
# Phase 4.11 --fast pixel gate: slices one at a time, realtime one state at a time. Evidence kept slim (logs + report.json).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-4-11/pixels
run() { # name glob
  n=$1; g=$2
  echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $EV/driver.log
  bin/pixel-parity --fast --only "$g" --out parity/out/p411gate-$n > $EV/$n.log 2>&1
  echo "$n rc=$?" >> $EV/driver.log
  mkdir -p $EV/$n
  find parity/out/p411gate-$n -name report.json -o -name '*.log' 2>/dev/null | while read f; do
    rel=${f#parity/out/p411gate-$n/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
  rm -rf parity/out/p411gate-$n
  KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
for s in auth errors pwa welcome bot_api search account users autocompletable messages interactions rooms; do run $s "$s/**"; done
for s in message_sent attachment_sent message_edited message_deleted boosted boost_removed typing/one typing/many unread_in_sidebar new_room_in_sidebar removed_from_room room_deleted_then_send presence_read; do run rt-${s//\//_} "realtime/$s"; done
echo done > $EV/STATUS
