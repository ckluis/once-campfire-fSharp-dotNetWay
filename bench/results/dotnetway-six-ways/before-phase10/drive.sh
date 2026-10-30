#!/usr/bin/env bash
# Inside colima, detached. Sessions in order; each: disk check, host probe + loadavg before, one harness run, probe after, status.
G=/Users/clank/Desktop/projects/upstream-campfire/scripts/guard.sh
O=/mnt/cfv/out
BASE=/var/tmp/campfire-bench-work/.hostprobe-baseline
HP=/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/lib/hostprobe.py
free_gb() { echo $(( $(df -k /Users/clank | awk 'NR==2{print $4}') / 1024 / 1024 )); }
source /Users/clank/cfsix-ws/ctl/env.sh
umask 002
APPS=rails,rust,c,fsharp,oncrack,dotnetway
for s in "$@"; do
  profile=${s%%-*}
  [ "$(free_gb)" -lt 15 ] && sudo fstrim -av >/dev/null 2>&1
  if [ "$(free_gb)" -lt 15 ]; then echo "blocked: disk $(free_gb)" > $O/$s.status; echo BLOCKED >> $O/driver.log; exit 1; fi
  echo "$(date +%T) $s start mac_free=$(free_gb)G" >> $O/driver.log
  { echo "before: $(python3 $HP 0-7 $BASE) load=$(cat /proc/loadavg)"; } > $O/$s.host
  if [ "$profile" = mixed ]; then extra="--mixed-write-rate 10 --routes room_show,messages_page,sidebar,search"
  else extra="--routes room_show,messages_page,sidebar,search,post_message"; fi
  ruby bin/benchmark --workspace /mnt/cfv/ws --rounds 3 --duration 8 --concurrencies 16 --cpus 0-3 --client-cpus 4-7 --apps $APPS $extra --output $O/$s > $O/$s.log 2>&1 &
  pid=$!
  taskset -c 4-7 $G $pid 12 >/dev/null 2>&1 &
  while kill -0 $pid 2>/dev/null; do echo "$(date +%T) load=$(cut -d' ' -f1-3 /proc/loadavg)" >> $O/$s.loadsamples; sleep 30; done
  wait $pid; rc=$?
  { echo "after: $(python3 $HP 0-7 $BASE) load=$(cat /proc/loadavg)"; } >> $O/$s.host
  echo $rc > $O/$s.status
  echo "$(date +%T) $s exit $rc" >> $O/driver.log
  [ "$rc" = 0 ] || { echo "stopped after failure" >> $O/driver.log; exit 1; }
  sleep 5
done
echo done > $O/driver.status
