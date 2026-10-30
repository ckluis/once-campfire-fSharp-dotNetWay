#!/usr/bin/env bash
B=/var/tmp/dnw-p10; cd $B
R=room_show,messages_page,sidebar
I=campfire-dotnetway:spike1
./run.sh s0-base rails,dotnetway $R 2 '{}' $I
./run.sh s1-open dotnetway $R 2 '{"SPIKE_OPEN_ONCE":"1"}' $I
./run.sh s2-cache dotnetway room_show 2 '{"SPIKE_PAGE_CACHE":"1"}' $I
./run.sh s3-both dotnetway $R 2 '{"SPIKE_OPEN_ONCE":"1","SPIKE_PAGE_CACHE":"1"}' $I
./run.sh s4-both-nolog dotnetway room_show 2 '{"SPIKE_OPEN_ONCE":"1","SPIKE_PAGE_CACHE":"1","LOG_REQUESTS":"false"}' $I
echo done > $B/out/seq2.status
