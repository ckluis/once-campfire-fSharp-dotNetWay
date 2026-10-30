#!/usr/bin/env bash
B=/var/tmp/dnw-p10; cd $B
R=room_show,messages_page,sidebar,search
./run.sh e0 rails,fsharp,dotnetway $R,post_message 2
./run.sh e1-nolog dotnetway $R 2 '{"LOG_REQUESTS":"false"}'
./run.sh e2-hill dotnetway $R 2 '{"DOTNET_HillClimbing_Disable":"0"}'
./run.sh e3-min16 dotnetway $R 2 '{"DOTNET_ThreadPool_ForceMinWorkerThreads":"16"}'
./run.sh e4-nolog-min16 dotnetway $R 2 '{"LOG_REQUESTS":"false","DOTNET_ThreadPool_ForceMinWorkerThreads":"16"}'
echo done > $B/out/seq1.status
