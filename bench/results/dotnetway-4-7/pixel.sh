#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-7
df -h / | tail -1 > $D/pixel-disk.txt
bin/pixel-parity --fast --only 'users/**,interactions/pwa_install**,interactions/notifications_help' --out $D/pixels/users > $D/pixel-users.log 2>&1
echo "users rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
bin/pixel-parity --fast --only 'auth/join**,pwa/install/**,interactions/translation_menu,rooms/opens/**,rooms/closeds/**' --out $D/pixels/extra > $D/pixel-extra.log 2>&1
echo "extra rc=$?" >> $D/STATUS-pixel-lines
bin/clean-parity > /dev/null 2>&1
echo done > $D/STATUS-pixel
