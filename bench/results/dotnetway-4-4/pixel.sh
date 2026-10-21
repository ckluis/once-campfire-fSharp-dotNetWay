#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-4
df -h / | tail -1 > $D/pixel-disk.txt
bin/pixel-parity --fast --only 'messages/**,interactions/actions_menu**,interactions/boost_**,interactions/quick_boost,interactions/edit_**,interactions/reply,interactions/copy_link,interactions/message_deleted' --out $D/pixels/main > $D/pixel-main.log 2>&1
echo "main rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
for s in message_sent message_edited message_deleted boosted boost_removed attachment_sent; do
  bin/pixel-parity --fast --seeds default --only "realtime/$s" --out $D/pixels/$s > $D/pixel-$s.log 2>&1
  echo "$s rc=$?" >> $D/STATUS-pixel-lines
  bin/clean-parity > /dev/null 2>&1
done
echo done > $D/STATUS-pixel
