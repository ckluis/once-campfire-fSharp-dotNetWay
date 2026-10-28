#!/usr/bin/env bash
B=/var/tmp/dnw-p10; C=$B/benchcopy
rm -rf $C; mkdir -p $C; cp -a /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench $C/
sed -i "s#^SEED=.*#SEED=$B/once-campfire-verification/fixtures/default#" $C/bench/run
sed -i 's#DOCKER_EXTRA_ARGS=(-v "$DIAG_DIR:/tmp")#DOCKER_EXTRA_ARGS=(-v "$DIAG_DIR:/tmp" $SPIKE_ENV)#' $C/bench/breakdown
mkdir -p $C/parity; cp $B/once-campfire-verification/fixtures/default/reference.env $C/parity/.env.reference; cd $C
export FSHARP_IMAGE=campfire-dotnetway:spike1
SPIKE_ENV="-e SPIKE_OPEN_ONCE=1" bench/breakdown perf --apps fsharp --routes room_show,sidebar --conc 16 --out $B/prof/perf-open > $B/prof/perf-open.log 2>&1
SPIKE_ENV="-e SPIKE_OPEN_ONCE=1 -e SPIKE_PAGE_CACHE=1" bench/breakdown perf --apps fsharp --routes room_show --conc 16 --out $B/prof/perf-hit > $B/prof/perf-hit.log 2>&1
SPIKE_ENV="" bench/breakdown perf --apps fsharp --routes room_show --conc 16 --out $B/prof/perf-base-hseed > $B/prof/perf-base-hseed.log 2>&1
echo done > $B/prof/perf2.status
