#!/usr/bin/env bash
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-p2-3
colima ssh -- bash -c "cd $PWD && mkdir -p /tmp/parity-net && export PARITY_NET_DIR=/tmp/parity-net && parity/bin/reference up --seed default --port 4311 --time 2026-03-02T16:00:00Z --freeze && parity/bin/candidate up --seed default --port 4312 --time 2026-03-02T16:00:00Z --freeze && python3 reference-tools/campfire/controllers_a/replay.py http://127.0.0.1:4311 http://127.0.0.1:4312" > $D/replay.log 2>&1
echo $? > $D/replay.status
colima ssh -- bash -c "cd $PWD && export PARITY_NET_DIR=/tmp/parity-net && parity/bin/reference down; parity/bin/candidate down" >> $D/replay.log 2>&1
echo done > $D/REPLAY
