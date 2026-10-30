#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-5
df -h / | tail -1 > $D/pixel-disk.txt
bin/pixel-parity --fast --only 'rooms/**,interactions/composer**,interactions/mention_autocomplete**' --out $D/pixels/main > $D/pixel-main.log 2>&1
echo "main rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
for s in removed_from_room room_deleted_then_send typing/one; do
  n=$(echo $s | tr / _)
  bin/pixel-parity --fast --seeds default --only "realtime/$s" --out $D/pixels/$n > $D/pixel-$n.log 2>&1
  echo "$s rc=$?" >> $D/STATUS-pixel-lines
  bin/clean-parity > /dev/null 2>&1
done
echo done > $D/STATUS-pixel
