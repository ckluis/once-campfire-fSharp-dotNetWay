#!/usr/bin/env bash
# Phase 10 gate, part B: the FULL pixel matrix (every seed, 3 engines, 4 viewports, light/dark, breakpoint sweep), sliced as Phase 4's was
# (bench/results/dotnetway-p4/full-slices.sh): the other four seeds whole, default by top-level state group, realtime one state at a time.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p10/full; mkdir -p $EV
run() { # name seed glob
  n=$1; seed=$2; g=$3
  [ -f $EV/$n.done ] && return
  for attempt in 1 2 3; do
    echo "=== $n seed $seed attempt $attempt $(date +%T) df $(df -h / | tail -1 | awk '{print $4}')" >> $EV/driver.log
    OUTD=$PWD/parity/out/p10full-$n
    if [ -n "$g" ]; then bin/pixel-parity --seeds $seed --only "$g" --out $OUTD -- --matrix full > $EV/$n.log 2>&1
    else bin/pixel-parity --seeds $seed --out $OUTD -- --matrix full > $EV/$n.log 2>&1; fi
    rc=$?
    echo "$n rc=$rc attempt=$attempt $(date +%T)" >> $EV/driver.log
    mkdir -p $EV/$n
    find $OUTD -name report.json 2>/dev/null | while read f; do rel=${f#$OUTD/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
    rm -rf $OUTD; KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
    grep -q "NO REPORT" $EV/$n.log || break
  done
  echo $rc > $EV/$n.done
}
for s in restricted crowd custom_styles first_run; do run $s $s ""; done
for s in errors pwa welcome bot_api search account users autocompletable auth messages interactions rooms; do run $s default "$s/**"; done
for s in message_sent attachment_sent message_edited message_deleted boosted boost_removed typing/one typing/many unread_in_sidebar new_room_in_sidebar removed_from_room room_deleted_then_send presence_read; do run rt-${s//\//_} default "realtime/$s"; done
echo done > $EV/STATUS
