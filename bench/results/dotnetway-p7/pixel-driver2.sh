#!/usr/bin/env bash
# Phase 7 pixel gate: (A) every state --fast, (B) lean matrix; slices one at a time, realtime one state at a time. Slim evidence.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p7/pixels
run() { # name glob [flags]
  n=$1; g=$2; fl=${3:-}
  echo "=== (cont) $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $EV/driver.log
  bin/pixel-parity $fl --only "$g" --out parity/out/p77-$n > $EV/$n.log 2>&1
  echo "$n rc=$?" >> $EV/driver.log
  mkdir -p $EV/$n
  find parity/out/p77-$n \( -name report.json -o -name '*.log' \) 2>/dev/null | while read f; do
    rel=${f#parity/out/p77-$n/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
  rm -rf parity/out/p77-$n
  KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
RT="message_sent attachment_sent message_edited message_deleted boosted boost_removed typing/one typing/many unread_in_sidebar new_room_in_sidebar removed_from_room room_deleted_then_send presence_read"
DIRS="auth errors pwa welcome bot_api search account users autocompletable messages interactions rooms"
run lean-rooms "rooms/**" "--seeds default"
for s in $RT; do run lean-rt-${s//\//_} "realtime/$s" "--seeds default"; done
run lean-otherseeds "**" "--seeds crowd,custom_styles,first_run,restricted"
echo done > $EV/STATUS
