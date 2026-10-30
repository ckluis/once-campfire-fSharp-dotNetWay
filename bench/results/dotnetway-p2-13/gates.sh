#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-13
echo running > $D/GATES
colima ssh -- bash -c "cd $PWD && CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build" > $D/build.log 2>&1
echo $? > $D/build.status
bin/pixel-parity --fast > $D/pixel.log 2>&1; echo $? > $D/pixel.status
bin/clean-parity > /dev/null 2>&1
: > $D/realtime.log
for state in message_sent attachment_sent message_edited message_deleted boosted boost_removed typing/one typing/many unread_in_sidebar new_room_in_sidebar removed_from_room room_deleted_then_send presence_read; do
  echo "=== realtime/$state" >> $D/realtime.log
  bin/pixel-parity --fast --seeds default --only "realtime/$state" >> $D/realtime.log 2>&1; echo "status $?" >> $D/realtime.log
  bin/clean-parity > /dev/null 2>&1
done
echo done > $D/realtime.status
: > $D/replay.log
for seed in default crowd first_run; do
  colima ssh -- bash -c "cd $PWD && export PARITY_NET_DIR=/tmp/parity-net && parity/bin/reference down >/dev/null 2>&1; parity/bin/candidate down >/dev/null 2>&1; parity/bin/reference up --seed $seed --port 4311 --time 2026-03-02T16:00:00Z --freeze && parity/bin/candidate up --seed $seed --port 4312 --time 2026-03-02T16:00:00Z --freeze && python3 reference-tools/campfire/controllers_a/replay.py http://127.0.0.1:4311 http://127.0.0.1:4312 --seed $seed; parity/bin/reference down >/dev/null 2>&1; parity/bin/candidate down >/dev/null 2>&1" >> $D/replay.log 2>&1
done
echo done > $D/replay.status
bin/clean-docker > /dev/null 2>&1
echo done > $D/GATES
