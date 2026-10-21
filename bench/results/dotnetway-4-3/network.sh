#!/usr/bin/env bash
# Network layer on the image: Content-Encoding for gzip, br, "gzip, br" and identity on the room page and the messages page.
set -u
cd "$(dirname "$0")/../../.."
P=4312
IMG=${DOTNETWAY_IMAGE:-campfire-dotnetway:app}
parity/bin/candidate down --all >/dev/null 2>&1
parity/bin/candidate up --seed default --port $P >/dev/null 2>&1 || { echo "up failed"; exit 1; }
for i in $(seq 1 60); do curl -fs http://127.0.0.1:$P/up >/dev/null && break; sleep 1; done
J=$(mktemp)
curl -s -c $J -o /dev/null -w 'login %{http_code}\n' -d 'email_address=david@37signals.com&password=secret123456' http://127.0.0.1:$P/session
ROOM=486777696
LAST=$(curl -s -b $J http://127.0.0.1:$P/rooms/$ROOM | grep -o 'message-id="[0-9]*"' | sed -n '30p' | sed 's/[^0-9]//g')
echo "room $ROOM, newest message $LAST"
for path in "/rooms/$ROOM" "/rooms/$ROOM/messages?before=$LAST"; do
  for enc in "" "identity" "gzip" "br" "gzip, br"; do
    for rep in 1 2; do
      if [ -z "$enc" ]; then H=(); else H=(-H "Accept-Encoding: $enc"); fi
      curl -s -b $J "${H[@]}" -D $J.h -o $J.body -w '' "http://127.0.0.1:$P$path"
      st=$(head -1 $J.h | tr -d '\r')
      n=$(grep -ci '^content-encoding:' $J.h)
      ce=$(grep -i '^content-encoding:' $J.h | tr -d '\r' | sed 's/^[^:]*: //' | paste -sd'|' -)
      vary=$(grep -i '^vary:' $J.h | tr -d '\r' | sed 's/^[^:]*: //' | paste -sd'|' -)
      cl=$(grep -i '^content-length:' $J.h | tr -d '\r' | sed 's/^[^:]*: //')
      size=$(wc -c < $J.body | tr -d ' ')
      case "$ce" in gzip) dec=$(gzip -dc $J.body | wc -c | tr -d ' ');; br) dec=$(node -e "process.stdout.write(String(require('zlib').brotliDecompressSync(require('fs').readFileSync('$J.body')).length))" 2>/dev/null || echo "n/a");; *) dec=$size;; esac
      echo "$IMG $path accept='$enc' rep=$rep $st content-encoding-headers=$n [$ce] vary=[$vary] content-length=$cl body=$size decoded=$dec"
    done
  done
done
parity/bin/candidate down --all >/dev/null 2>&1
rm -f $J $J.h $J.body
