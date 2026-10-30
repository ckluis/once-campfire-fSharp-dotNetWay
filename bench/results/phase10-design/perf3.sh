#!/usr/bin/env bash
B=/var/tmp/dnw-p10; C=$B/benchcopy; cd $C
export FSHARP_IMAGE=campfire-dotnetway:spike3
SPIKE_ENV="-e SPIKE_OPEN_ONCE=1 -e DOTNET_TC_CallCountingDelayMs=0" bench/breakdown perf --apps fsharp --routes room_show,sidebar --conc 16 --out $B/prof/perf-A > $B/prof/perf-A.log 2>&1
SPIKE_ENV="-e SPIKE_OPEN_ONCE=1 -e DOTNET_TC_CallCountingDelayMs=0 -e SPIKE_PAGE_CACHE=1" bench/breakdown perf --apps fsharp --routes room_show,sidebar --conc 16 --out $B/prof/perf-AB > $B/prof/perf-AB.log 2>&1
echo done > $B/prof/perf3.status
