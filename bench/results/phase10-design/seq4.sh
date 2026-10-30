#!/usr/bin/env bash
B=/var/tmp/dnw-p10; cd $B
R=room_show,messages_page,sidebar,search
I=campfire-dotnetway:spike2
./run.sh u0-ctl rails,fsharp,dotnetway $R,post_message 2
./run.sh u1-A dotnetway $R,post_message 2 '{"SPIKE_OPEN_ONCE":"1","DOTNET_TC_CallCountingDelayMs":"0"}' $I
./run.sh u2-AB dotnetway $R 2 '{"SPIKE_OPEN_ONCE":"1","DOTNET_TC_CallCountingDelayMs":"0","SPIKE_PAGE_CACHE":"1"}' $I
MIXED=1 ./run.sh u3-AB-mixed rails,dotnetway $R 2 '{"SPIKE_OPEN_ONCE":"1","DOTNET_TC_CallCountingDelayMs":"0","SPIKE_PAGE_CACHE":"1"}' $I
echo done > $B/out/seq4.status
