#!/usr/bin/env bash
# Phase 9 gate, part A: image from HEAD (label checked), bin/verify, bin/linux-tests App, AC11 on Pebble, pebble-image.sh.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
export PATH="$HOME/.dotnet:$PATH"
D=$PWD/bench/results/dotnetway-p9
log() { echo "=== $* $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log; }
H=$(git rev-parse HEAD)
log build
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H label=$(colima ssh -- docker image inspect campfire-dotnetway:app --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')" >> $D/driver.log
bin/clean-docker > $D/clean-docker1.log 2>&1
log verify
CAMPFIRE_REQUIRE_SEED=1 bin/verify > $D/verify.log 2>&1; echo "verify rc=$?" >> $D/driver.log
log linux-tests
colima ssh -- bash -c "cd $PWD && bin/linux-tests App" > $D/linux-tests.log 2>&1; echo "linux-tests rc=$?" >> $D/driver.log
log ac11
colima ssh -- bash -c 'mkdir -p /var/tmp/pebble && (docker rm -f pebble >/dev/null 2>&1; true) && docker run -d --name pebble --network host --add-host campfire.test:127.0.0.1 -e PEBBLE_VA_NOSLEEP=1 -e PEBBLE_WFE_NONCEREJECT=0 ghcr.io/letsencrypt/pebble:latest && sleep 3 && docker cp pebble:/test/certs/pebble.minica.pem /var/tmp/pebble/pebble.minica.pem' > $D/ac11.log 2>&1
colima ssh -- bash -c "cd $PWD && LINUX_TESTS_DOCKER_ARGS='--network host --volume /var/tmp/pebble:/pebble' PEBBLE_DIRECTORY=https://localhost:14000/dir PEBBLE_ROOT=/pebble/pebble.minica.pem PEBBLE_TIMES=/pebble/times bin/linux-tests App --filter-method '*AC11*'" >> $D/ac11.log 2>&1; echo "ac11 rc=$?" >> $D/driver.log
colima ssh -- docker rm -f pebble >> $D/ac11.log 2>&1
log pebble-image
colima ssh -- bash -c "cd $PWD && reference-tools/front/pebble-image.sh campfire-dotnetway:app" > $D/pebble-image.log 2>&1; echo "pebble-image rc=$?" >> $D/driver.log
bin/clean-docker > $D/clean-docker2.log 2>&1
log done
echo done > $D/STATUS-A
