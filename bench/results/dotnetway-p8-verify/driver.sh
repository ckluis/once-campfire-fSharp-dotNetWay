#!/usr/bin/env bash
# Phase 8 independent verification: image of the closing commit; bench against the gate's image (by ID); pixel slices; cookie
# round trip; Phase 7's Host conditions (bin/linux-tests App, AC11 on Pebble, pebble-image.sh). One thing at a time in colima.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=$PWD/bench/results/dotnetway-p8-verify
log() { echo "=== $* $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log; }
H=$(git rev-parse HEAD)
OLD=$(colima ssh -- docker image inspect campfire-dotnetway:app --format '{{.Id}}')
OLDREV=$(colima ssh -- docker image inspect campfire-dotnetway:app --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')
echo "control image $OLD revision $OLDREV" >> $D/driver.log
log build
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H label=$(colima ssh -- docker image inspect campfire-dotnetway:app --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')" >> $D/driver.log
# bench: new (HEAD) vs the gate's image, post and sidebar (the thinnest cells), 3 reps interleaved
Q=$D/quick; mkdir -p $Q
brun() { log "bench $1 rep $3"; colima ssh -- bash -c "cd $PWD && DOTNETWAY_IMAGE=$2 bench/quick --reps 3 --routes post_message,sidebar --only dotnetway:$3 --out $Q/$1" >> $Q/bench-$1.log 2>&1; echo "bench $1 $3 rc=$?" >> $D/driver.log; }
N=campfire-dotnetway:app
brun new $N 1; brun ctl $OLD 1
brun ctl $OLD 2; brun new $N 2
brun new $N 3; brun ctl $OLD 3
for l in new ctl; do
  colima ssh -- bash -c "cd $PWD && bench/quick report $Q/$l" >> $Q/bench-$l.log 2>&1
  colima ssh -- bash -c "cd $PWD && bench/quick compare $Q/$l" > $Q/compare-$l.txt 2>&1
done
# pixels
P=$D/pixels; mkdir -p $P
prun() { n=$1; g=$2; fl=${3:-}; log "pixel $n"
  bin/pixel-parity $fl --only "$g" --out parity/out/p8v-$n > $P/$n.log 2>&1; echo "pixel $n rc=$?" >> $D/driver.log
  mkdir -p $P/$n; find parity/out/p8v-$n -name report.json | while read f; do rel=${f#parity/out/p8v-$n/}; mkdir -p "$P/$n/$(dirname "$rel")"; cp "$f" "$P/$n/$rel"; done
  rm -rf parity/out/p8v-$n; KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1; }
prun fast 'auth/**,users/**,bot_api/**,account/**,pwa/**,rooms/show/designers' --fast
prun rt-message_sent 'realtime/message_sent' '--fast --seeds default'
log cookie
bin/cookie-roundtrip > $D/cookie-roundtrip.log 2>&1; echo "cookie rc=$?" >> $D/driver.log
# Phase 7's condition for a Web/Host.fs change (8.8): HS2/HS4 on Linux, AC11 on Pebble, the image against Pebble
log linux-tests
colima ssh -- bash -c "cd $PWD && bin/linux-tests App" > $D/linux-tests.log 2>&1; echo "linux-tests rc=$?" >> $D/driver.log
log ac11
colima ssh -- bash -c 'mkdir -p /var/tmp/pebble && (docker rm -f pebble >/dev/null 2>&1; true) && docker run -d --name pebble --network host --add-host campfire.test:127.0.0.1 -e PEBBLE_VA_NOSLEEP=1 -e PEBBLE_WFE_NONCEREJECT=0 ghcr.io/letsencrypt/pebble:latest && sleep 3 && docker cp pebble:/test/certs/pebble.minica.pem /var/tmp/pebble/pebble.minica.pem' > $D/ac11.log 2>&1
colima ssh -- bash -c "cd $PWD && LINUX_TESTS_DOCKER_ARGS='--network host --volume /var/tmp/pebble:/pebble' PEBBLE_DIRECTORY=https://localhost:14000/dir PEBBLE_ROOT=/pebble/pebble.minica.pem PEBBLE_TIMES=/pebble/times bin/linux-tests App --filter-method '*AC11*'" >> $D/ac11.log 2>&1; echo "ac11 rc=$?" >> $D/driver.log
colima ssh -- docker rm -f pebble >> $D/ac11.log 2>&1
log pebble-image
colima ssh -- bash -c "cd $PWD && reference-tools/front/pebble-image.sh campfire-dotnetway:app" > $D/pebble-image.log 2>&1; echo "pebble-image rc=$?" >> $D/driver.log
log clean
bin/clean-docker > $D/clean-docker.log 2>&1; KEEP_MINUTES=0 bin/clean-parity >/dev/null 2>&1
log done
echo done > $D/STATUS
