#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-9-4
FSHARP_IMAGE=campfire-dotnetway:app bench/breakdown profile --routes post_message,room_show --out $D/profile-app > $D/profile-app.log 2>&1
echo "rc=$?" > $D/STATUS-prof
