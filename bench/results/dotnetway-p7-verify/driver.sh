#!/usr/bin/env bash
# Phase 7 independent verification on the image built from HEAD: Pebble end to end, asset-check, cookie round trip,
# pixel slices (TLS lean, front-touched --fast on every seed, one realtime state), then bench/quick HEAD vs p7gate
# (the gate's image, 607b31c: isolates the security review's log filter), interleaved, 3 reps.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
EV=$PWD/bench/results/dotnetway-p7-verify
log() { echo "$* $(date +%T) df $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $EV/driver.log; }
log "=== start"
colima ssh -- bash -c "cd $PWD && reference-tools/front/pebble-image.sh" > $EV/pebble-image.log 2>&1; log "pebble-image rc=$?"
colima ssh -- bash -c "cd $PWD && bin/asset-check" > $EV/asset-check.log 2>&1; log "asset-check rc=$?"
bin/cookie-roundtrip > $EV/cookie-roundtrip.log 2>&1; log "cookie-roundtrip rc=$?"
px() { # name glob flags
  n=$1; g=$2; fl=$3
  log "=== pixels $n"
  bin/pixel-parity $fl --only "$g" --out parity/out/p7v-$n > $EV/px-$n.log 2>&1
  log "pixels $n rc=$?"
  mkdir -p $EV/pixels/$n
  find parity/out/p7v-$n -name report.json 2>/dev/null | while read f; do
    rel=${f#parity/out/p7v-$n/}; mkdir -p "$EV/pixels/$n/$(dirname "$rel")"; cp "$f" "$EV/pixels/$n/$rel"; done
  rm -rf parity/out/p7v-$n
  KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
}
px tls 'auth/sign_in,rooms/show/designers' '--candidate-tls --seeds default'
px front 'users/**,account/**,auth/**,errors/**,rooms/show/designers' '--fast'
px rt-message_sent 'realtime/message_sent' '--fast --seeds default'
D=$EV/quick
ROUTES=post_message,sidebar,search,avatar
run() { # label image rep
  for part in 1/2 2/2; do
    log "bench $1 rep $3 part $part"
    colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --routes $ROUTES --only dotnetway:$3 --part $part --out $D/$1" >> $EV/bench-$1.log 2>&1
    log "rc=$?"
  done
}
N=campfire-dotnetway:app; C=campfire-dotnetway:p7gate
run new $N 1; run ctl $C 1
run ctl $C 2; run new $N 2
run new $N 3; run ctl $C 3
for l in new ctl; do
  colima ssh -- bash -c "cd $PWD && bench/quick report $D/$l" >> $EV/bench-$l.log 2>&1
  colima ssh -- bash -c "cd $PWD && bench/quick compare $D/$l" > $EV/compare-$l.txt 2>&1
done
log "=== done"
echo done > $EV/STATUS
