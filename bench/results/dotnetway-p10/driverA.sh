#!/usr/bin/env bash
# Phase 10 gate, part A: image from HEAD (label checked), control image from dcc9434 (p10ctl), bin/verify, cookie-roundtrip, db-differential, asset-check.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
export PATH="$HOME/.dotnet:$PATH"
D=$PWD/bench/results/dotnetway-p10
log() { echo "=== $* $(date +%T) mac-free $(df -h / | tail -1 | awk '{print $4}') load $(uptime | sed 's/.*averages: //')" >> $D/driver.log; }
H=$(git rev-parse HEAD)
log build
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$H parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? head=$H label=$(colima ssh -- docker image inspect campfire-dotnetway:app --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')" >> $D/driver.log
log control-build
CTX=$PWD/parity/out/p10ctl-ctx; rm -rf $CTX; mkdir -p $CTX
git archive dcc9434 global.json Directory.Build.props Directory.Packages.props src Dockerfile .dockerignore | tar -x -C $CTX
mkdir -p $CTX/reference; (cd reference && tar --exclude=.git -cf - .) | tar -x -C $CTX/reference
DC=$(git rev-parse dcc9434)
colima ssh -- bash -c "cd $CTX && docker build -t campfire-dotnetway:p10ctl -f Dockerfile --build-arg APP_VERSION=parity --build-arg GIT_REVISION=parity --build-arg SOURCE_REVISION=dcc9434 --label org.opencontainers.image.revision=$DC ." > $D/build-ctl.log 2>&1
echo "control build rc=$? label=$(colima ssh -- docker image inspect campfire-dotnetway:p10ctl --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')" >> $D/driver.log
rm -rf $CTX
bin/clean-docker > $D/clean-docker1.log 2>&1
log verify
CAMPFIRE_REQUIRE_SEED=1 bin/verify > $D/verify.log 2>&1; echo "verify rc=$?" >> $D/driver.log
log cookie-roundtrip
bin/cookie-roundtrip > $D/cookie-roundtrip.log 2>&1; echo "cookie rc=$?" >> $D/driver.log
log db-differential
bin/db-differential > $D/db-differential.log 2>&1; echo "dbdiff rc=$?" >> $D/driver.log
log asset-check
bin/asset-check > $D/asset-check.log 2>&1; echo "asset rc=$?" >> $D/driver.log
bin/clean-docker > $D/clean-docker2.log 2>&1
log done
echo done > $D/STATUS-A
