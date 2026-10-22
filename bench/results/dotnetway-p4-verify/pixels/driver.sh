#!/usr/bin/env bash
# Phase 4 verifier, image 49e1e09: (1) the two empty-404 states on the full matrix (all engines; Firefox was NOT CAPTURED at the gate),
# (2) --fast on the sidebar and room slices (the security review's linkToRoom change had no pixel run), every seed.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p4-verify/pixels
run() { # name seed glob args...
  n=$1; seed=$2; g=$3; shift 3
  OUTD=$PWD/parity/out/p4v-$n
  echo "=== $n $(date +%T) df $(df -h / | tail -1 | awk '{print $4}')" >> $EV/driver.log
  bin/pixel-parity --seeds $seed --only "$g" --out $OUTD "$@" > $EV/$n.log 2>&1
  echo "$n rc=$?" >> $EV/driver.log
  mkdir -p $EV/$n
  find $OUTD -name report.json 2>/dev/null | while read f; do rel=${f#$OUTD/}; mkdir -p "$EV/$n/$(dirname "$rel")"; cp "$f" "$EV/$n/$rel"; done
  rm -rf $OUTD; KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
run join-full default "auth/join/invalid_code" -- --matrix full
run restricted-full restricted "rooms/opens/new/restricted_member" -- --matrix full
run sidebar-fast default,crowd,custom_styles,first_run,restricted "interactions/sidebar**,interactions/phone_sidebar,rooms/show/**,search/**" --fast
echo done > $EV/STATUS
