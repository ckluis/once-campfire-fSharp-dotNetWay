#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-3-3
for s in message_sent typing/one presence_read removed_from_room; do
  n=$(echo $s | tr / _)
  bin/pixel-parity --fast --seeds default --only "realtime/$s" --out $D/pixels/$n > $D/pixel-$n.log 2>&1
  echo "$s rc=$?" >> $D/STATUS-realtime-lines
  bin/clean-parity > /dev/null 2>&1
done
echo done > $D/STATUS-realtime
