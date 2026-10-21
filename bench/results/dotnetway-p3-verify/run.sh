#!/usr/bin/env bash
# Phase 3 verification: image from HEAD (label checked), realtime states that open subscriptions, one per run; then the cable bench.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p3-verify
rev=$(git rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$rev parity/bin/candidate build" > $D/build.log 2>&1
echo "build rc=$? rev=$rev" > $D/STATUS-build
colima ssh -- docker image inspect campfire-dotnetway:app --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' >> $D/STATUS-build 2>&1
bin/clean-docker > $D/clean-docker.log 2>&1
for s in message_sent typing/one presence_read removed_from_room room_deleted_then_send unread_in_sidebar new_room_in_sidebar; do
  n=$(echo $s | tr / _)
  bin/pixel-parity --fast --seeds default --only "realtime/$s" --out $D/pixels/$n > $D/pixel-$n.log 2>&1
  echo "$s rc=$?" >> $D/STATUS-realtime-lines
  bin/clean-parity > /dev/null 2>&1
done
{ uptime; bench/cable/run fsharp --reps 2 --plain; echo "cable rc=$?"; uptime; } > $D/cable-run.log 2>&1
echo done > $D/STATUS
