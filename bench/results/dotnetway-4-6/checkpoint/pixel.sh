#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-6/checkpoint
df -h / | tail -1 > $D/pixel-disk.txt
bin/pixel-parity --fast --only 'rooms/show/**,search/**,interactions/sidebar**' --out $D/pixels/main > $D/pixel-main.log 2>&1
echo "main rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
bin/pixel-parity --fast --seeds default --only "realtime/message_sent" --out $D/pixels/message_sent > $D/pixel-message_sent.log 2>&1
echo "message_sent rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
echo done > $D/STATUS-pixel
