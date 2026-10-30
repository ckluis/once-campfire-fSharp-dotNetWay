#!/usr/bin/env bash
B=/var/tmp/dnw-p10; cd $B
R=room_show,messages_page,sidebar,search
I=campfire-dotnetway:spike3
E='{"SPIKE_OPEN_ONCE":"1","DOTNET_TC_CallCountingDelayMs":"0","SPIKE_PAGE_CACHE":"1"}'
./run.sh v0-AB rails,dotnetway $R,post_message 2 "$E" $I
MIXED=1 ./run.sh v1-AB-mixed rails,dotnetway $R 2 "$E" $I
./run.sh v2-AB-nolog dotnetway $R 2 '{"SPIKE_OPEN_ONCE":"1","DOTNET_TC_CallCountingDelayMs":"0","SPIKE_PAGE_CACHE":"1","LOG_REQUESTS":"false"}' $I
echo done > $B/out/seq5.status
