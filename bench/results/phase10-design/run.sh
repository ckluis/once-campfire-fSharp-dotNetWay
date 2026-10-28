#!/usr/bin/env bash
# VM-side: run.sh NAME APPS ROUTES ROUNDS [DNW_ENV_JSON] [DNW_IMAGE]; writes $B/out/NAME.status
B=/var/tmp/dnw-p10
name=$1 apps=$2 routes=$3 rounds=$4 envj=${5:-} img=${6:-campfire-dotnetway:app}
export FSHARP_IMAGE=campfire-fsharp:app DOTNETWAY_IMAGE=$img RAILS_IMAGE=campfire-rails-upstream:ca9b963
export RAILS_BENCH_ENV='{"WEB_CONCURRENCY":"4","RAILS_MAX_THREADS":"1","RAILS_MIN_THREADS":"1"}'
[ -n "$envj" ] && export DOTNETWAY_BENCH_ENV="$envj"
export TMPDIR=$B
cd $B/once-campfire-verification
rm -rf $B/out/$name
ruby bin/benchmark --workspace $B/ws --rounds $rounds --duration 8 --concurrencies 16 --cpus 0-3 --client-cpus 4-7 --apps $apps --routes $routes ${MIXED:+--mixed-write-rate 10} --output $B/out/$name > $B/out/$name.log 2>&1
echo $? > $B/out/$name.status
