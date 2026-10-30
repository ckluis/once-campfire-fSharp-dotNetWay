#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-3
df -h / | tail -1 > $D/pixel-disk.txt
bin/pixel-parity --fast --only 'rooms/**,messages/**,search/**,interactions/sidebar*' --out $D/pixels/main > $D/pixel-main.log 2>&1
echo "main rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
for s in message_sent boosted message_edited; do
  bin/pixel-parity --fast --seeds default --only "realtime/$s" --out $D/pixels/$s > $D/pixel-$s.log 2>&1
  echo "$s rc=$?" >> $D/STATUS-pixel-lines
  bin/clean-parity > /dev/null 2>&1
done
echo done > $D/STATUS-pixel
