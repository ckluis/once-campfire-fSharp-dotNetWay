#!/usr/bin/env bash
# Mints a session_token cookie from our app (default seed), writes minted-output.json = the Rust vectors with that session.
set -euo pipefail
ROOT=/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=$ROOT/bench/results/dotnetway-p2
SEED=$ROOT/parity/.seed/default; PORT=4521; BASE=http://127.0.0.1:$PORT; W=$(mktemp -d /var/tmp/mint.XXXXXX)
mkdir -p $W/db $W/storage; cp -a $SEED/db/. $W/db/; cp -a $SEED/storage/. $W/storage/
trap 'docker rm -f mint-app >/dev/null 2>&1; rm -rf $W' EXIT
lab() { python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' $SEED/labels.json "$1"; }
mapfile -t args < <(grep -Ev '^(#|$|WEB_CONCURRENCY|JOB_CONCURRENCY|RAILS_MAX_THREADS|RAILS_LOG_LEVEL)' $ROOT/parity/.env.reference | sed 's/^/-e\n/')
docker run -d --name mint-app --user "$(id -u):$(id -g)" --network host -e HTTP_PORT=$PORT -e TARGET_PORT=$((PORT+1)) "${args[@]}" -e WEB_CONCURRENCY=1 -e JOB_CONCURRENCY=1 -e RAILS_LOG_LEVEL=warn -v $W/db:/rails/storage/db -v $W/storage:/rails/storage/files campfire-dotnetway:app >/dev/null
for _ in $(seq 1 480); do curl -fsS -o /dev/null $BASE/up 2>/dev/null && break; sleep 0.25; done
J=$W/jar; UA="mint/1"
tok=$(curl -fsS -A $UA -b $J -c $J $BASE/session/new | python3 -c 'import re,sys,html;m=re.search(r"name=\"csrf-token\"[^>]*content=\"([^\"]*)\"|content=\"([^\"]*)\"[^>]*name=\"csrf-token\"",sys.stdin.read());print(html.unescape(m.group(1) or m.group(2)) if m else "")')
curl -sS -D $D/minted-headers.txt -o /dev/null -A $UA -b $J -c $J -H "Origin: $BASE" -H 'Sec-Fetch-Site: same-origin' --data-urlencode "authenticity_token=$tok" --data-urlencode "email_address=$(lab emails.david)" --data-urlencode "password=$(lab passwords.all)" $BASE/session
python3 - $D/minted-headers.txt $ROOT/target/rails_compat_rust_output.json $D/minted-output.json <<'PY'
import sys,re,json,base64,urllib.parse
h=open(sys.argv[1]).read()
raw=re.search(r'(?im)^set-cookie: session_token=([^;]+)',h).group(1)
cookie=urllib.parse.unquote(raw)
msg=json.loads(base64.b64decode(cookie.split('--')[0]))['_rails']['message']
token=json.loads(base64.b64decode(msg))
o=json.load(open(sys.argv[2])); o['session']={'session_token':token,'session_token_cookie':cookie}
json.dump(o,open(sys.argv[3],'w'))
print("minted token", token)
PY
