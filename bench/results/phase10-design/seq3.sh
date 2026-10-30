#!/usr/bin/env bash
B=/var/tmp/dnw-p10; cd $B
R=room_show,messages_page,sidebar
./run.sh t0-ctl dotnetway $R 2 '{}'
./run.sh t1-nopgo dotnetway $R 2 '{"DOTNET_TieredPGO":"0"}'
./run.sh t2-nodelay dotnetway $R 2 '{"DOTNET_TC_CallCountingDelayMs":"0"}'
./run.sh t3-fulljit dotnetway $R 2 '{"DOTNET_TieredCompilation":"0","DOTNET_ReadyToRun":"0"}'
echo done > $B/out/seq3.status
