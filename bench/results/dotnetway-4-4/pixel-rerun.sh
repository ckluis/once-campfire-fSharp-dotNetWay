#!/usr/bin/env bash
# The main slice again on the image of ab76f64 (the editor-space fix).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-4
bin/pixel-parity --fast --only 'messages/**,interactions/actions_menu**,interactions/boost_**,interactions/quick_boost,interactions/edit_**,interactions/reply,interactions/copy_link,interactions/message_deleted' --out $D/pixels/main2 > $D/pixel-main.log 2>&1
echo "main rc=$?" > $D/STATUS-pixel-rerun
bin/clean-parity > /dev/null 2>&1
