#!/usr/bin/env bash
# Phase 8 pixel gate: (A) every state --fast, (B) lean matrix; slices one at a time, realtime one state at a time. Slim evidence.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p8/pixels
run() { # name glob [flags]
  n=$1; g=$2; fl=${3:-}
  echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $EV/driver.log
  bin/pixel-parity $fl --only "$g" --out parity/out/p88-$n > $EV/$n.log 2>&1
  echo "$n rc=$?" >> $EV/driver.log
  mkdir -p $EV/$n
  find parity/out/p88-$n \( -name report.json -o -name '*.log' \) 2>/dev/null | while read f; do
    rel=${f#parity/out/p88-$n/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
  rm -rf parity/out/p88-$n
  KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
run tls "auth/sign_in,rooms/show/designers" "--candidate-tls --seeds default"
RT="message_sent attachment_sent message_edited message_deleted boosted boost_removed typing/one typing/many unread_in_sidebar new_room_in_sidebar removed_from_room room_deleted_then_send presence_read"
DIRS="auth errors pwa welcome bot_api search account users autocompletable messages interactions rooms"
for s in $DIRS; do run lean-$s "$s/**" "--seeds default"; done
for s in $RT; do run lean-rt-${s//\//_} "realtime/$s" "--seeds default"; done
run lean-otherseeds "**" "--seeds crowd,custom_styles,first_run,restricted"
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
bin/cookie-roundtrip > bench/results/dotnetway-p8/cookie-roundtrip.log 2>&1; echo "cookie rc=$?" >> $EV/driver.log
bin/db-differential > bench/results/dotnetway-p8/db-differential.log 2>&1; echo "dbdiff rc=$?" >> $EV/driver.log
echo done > $EV/STATUS
