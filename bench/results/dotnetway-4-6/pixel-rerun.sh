#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-6
df -h / | tail -1 > $D/pixel-disk.txt
bin/pixel-parity --fast --only 'interactions/sidebar**,interactions/phone_sidebar,search/**,autocompletable/**' --out $D/pixels/main > $D/pixel-main.log 2>&1
echo "main rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
for s in unread_in_sidebar new_room_in_sidebar presence_read; do
  bin/pixel-parity --fast --seeds default --only "realtime/$s" --out $D/pixels/$s > $D/pixel-$s.log 2>&1
  echo "$s rc=$?" >> $D/STATUS-pixel-lines
  bin/clean-parity > /dev/null 2>&1
done
echo done > $D/STATUS-pixel
