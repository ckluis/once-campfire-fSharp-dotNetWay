#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
S=/var/tmp/dnw-p10/prof
FSHARP_IMAGE=campfire-dotnetway:app bench/breakdown profile --routes room_show,sidebar --out $S/profile-dnw > $S/profile-dnw.log 2>&1; echo "profile rc=$?" >> $S/STATUS1
FSHARP_IMAGE=campfire-dotnetway:app bench/breakdown perf --apps fsharp --routes room_show,sidebar,up --conc 16 --out $S/perf-dnw > $S/perf-dnw.log 2>&1; echo "perf dnw rc=$?" >> $S/STATUS1
FSHARP_IMAGE=campfire-fsharp:app bench/breakdown perf --apps fsharp --routes room_show,sidebar --conc 16 --out $S/perf-port > $S/perf-port.log 2>&1; echo "perf port rc=$?" >> $S/STATUS1
echo done >> $S/STATUS1
