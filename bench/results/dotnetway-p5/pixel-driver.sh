#!/usr/bin/env bash
# Phase 5 pixel gate: slices one at a time, realtime one state at a time. Evidence kept slim (logs + report.json).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p5/pixels
run() { # name glob [extra pixel-parity flags]
  n=$1; g=$2; fl=${3:-}
  echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $EV/driver.log
  bin/pixel-parity $fl --only "$g" --out parity/out/p5lean-$n > $EV/$n.log 2>&1
  echo "$n rc=$?" >> $EV/driver.log
  mkdir -p $EV/$n
  find parity/out/p5lean-$n -name report.json -o -name '*.log' 2>/dev/null | while read f; do
    rel=${f#parity/out/p5lean-$n/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
  rm -rf parity/out/p5lean-$n
  KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
run fast "rooms/show/**,messages/**,search/**,interactions/edit_form*" --fast
for s in auth errors pwa welcome bot_api search account users autocompletable messages interactions rooms; do run $s "$s/**"; done
for s in message_sent attachment_sent message_edited message_deleted boosted boost_removed typing/one typing/many unread_in_sidebar new_room_in_sidebar removed_from_room room_deleted_then_send presence_read; do run rt-${s//\//_} "realtime/$s"; done
echo done > $EV/STATUS
