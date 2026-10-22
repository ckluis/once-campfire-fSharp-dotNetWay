#!/usr/bin/env bash
# Full matrix, default and restricted seeds, in slices with a retry when the harness process crashes (NO REPORT:
# "apiRequestContext.get: Target page, context or browser has been closed" in NetworkLog.bodyOf, an unhandled rejection).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p4/full
run() { # name seed glob
  n=$1; seed=$2; g=$3
  for attempt in 1 2 3; do
    echo "=== $n seed $seed attempt $attempt $(date +%T) df $(df -h / | tail -1 | awk '{print $4}')" >> $EV/driver.log
    OUTD=$PWD/parity/out/p4full-$n
    if [ -n "$g" ]; then bin/pixel-parity --seeds $seed --only "$g" --out $OUTD -- --matrix full > $EV/$n.log 2>&1
    else bin/pixel-parity --seeds $seed --out $OUTD -- --matrix full > $EV/$n.log 2>&1; fi
    rc=$?
    echo "$n rc=$rc attempt=$attempt" >> $EV/driver.log
    mkdir -p $EV/$n
    find $OUTD -name report.json -o -name '*.log' 2>/dev/null | while read f; do rel=${f#$OUTD/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
    rm -rf $OUTD; KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
    grep -q "NO REPORT" $EV/$n.log || break
  done
}
run restricted restricted ""
for s in auth errors pwa welcome bot_api search account users autocompletable messages interactions rooms; do run $s default "$s/**"; done
for s in message_sent attachment_sent message_edited message_deleted boosted boost_removed typing/one typing/many unread_in_sidebar new_room_in_sidebar removed_from_room room_deleted_then_send presence_read; do run rt-${s//\//_} default "realtime/$s"; done
echo done > $EV/STATUS
