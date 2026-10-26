#!/usr/bin/env bash
# Phase 7 unit 7.5 (plans/design-front.md F15/F16): the production image against a real ACME server (Pebble), end to end.
#
#   reference-tools/front/pebble-image.sh [IMAGE]      IMAGE defaults to campfire-dotnetway:app (build it first: parity/bin/candidate build)
#
# Pebble and the image run on the VM's host network. The image gets TLS_DOMAIN=campfire.test, HTTP_PORT=5002 (Pebble's HTTP-01 port),
# HTTPS_PORT=8443, ACME_DIRECTORY=Pebble's, and SSL_CERT_FILE = Pebble's minica root (so the app's "acme" HttpClient trusts the
# directory). It checks, and exits 0 only if all hold:
#   1. the image orders a certificate at boot (Pebble issued exactly one);
#   2. storage/thruster/campfire.test and acme_account+key exist, both 0600, in a 0700 directory;
#   3. the HTTPS port serves a 2-certificate chain (leaf and intermediate) for SNI campfire.test (openssl s_client -showcerts);
#   4. after `docker restart` the certificate is loaded from the cache and Pebble issued no second one.
# Containers, the Pebble root and the storage directory are removed at the end (the Pebble image stays: bin/clean-docker is the unit's close).
# On the Mac it runs itself inside colima.
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
if [ "$(uname -s)" = Darwin ] && [ -z "${PEBBLE_IMAGE_IN_VM:-}" ]; then
  command -v colima >/dev/null || { echo "pebble-image: needs colima" >&2; exit 2; }
  exec colima ssh -- bash -c "cd '$ROOT' && PEBBLE_IMAGE_IN_VM=1 reference-tools/front/pebble-image.sh $(printf '%q ' "$@")"
fi

IMAGE=${1:-campfire-dotnetway:app}
PEBBLE=${PEBBLE_CONTAINER_IMAGE:-ghcr.io/letsencrypt/pebble:latest}
WORK=$(mktemp -d /var/tmp/pebble-image.XXXXXX)
APP=campfire-pebble-app PEB=campfire-pebble-ca
DOMAIN=campfire.test HTTPS_PORT=8443
fail=0
ok()  { echo "ok   $*"; }
bad() { echo "FAIL $*"; fail=1; }
cleanup() { docker rm -f "$APP" "$PEB" >/dev/null 2>&1 || true; rm -rf "$WORK"; }
trap cleanup EXIT

docker rm -f "$APP" "$PEB" >/dev/null 2>&1 || true
docker run -d --name "$PEB" --network host --add-host "$DOMAIN:127.0.0.1" \
  -e PEBBLE_VA_NOSLEEP=1 -e PEBBLE_WFE_NONCEREJECT=0 "$PEBBLE" >/dev/null
docker cp "$PEB:/test/certs/pebble.minica.pem" "$WORK/pebble.minica.pem"
mkdir -p "$WORK/storage"
for _ in $(seq 1 60); do curl -sk -o /dev/null https://localhost:14000/dir && break; sleep 0.5; done

# The environment the parity candidate has (test-only keys), without DISABLE_SSL: this is a TLS install.
envfile=$WORK/env; grep -v '^DISABLE_SSL' "$ROOT/parity/.env.reference" | grep -v '^#' > "$envfile"
docker run -d --name "$APP" --network host --add-host "$DOMAIN:127.0.0.1" --user "$(id -u):$(id -g)" \
  --env-file "$envfile" \
  -e TLS_DOMAIN=$DOMAIN -e HTTP_PORT=5002 -e HTTPS_PORT=$HTTPS_PORT -e TARGET_PORT=3999 \
  -e ACME_DIRECTORY=https://localhost:14000/dir -e SSL_CERT_FILE=/pebble/pebble.minica.pem \
  -v "$WORK/pebble.minica.pem:/pebble/pebble.minica.pem:ro" -v "$WORK/storage:/rails/storage" \
  "$IMAGE" >/dev/null

issued() { docker logs "$PEB" 2>&1 | grep -c 'Issued certificate' || true; }
chain()  { echo | openssl s_client -connect 127.0.0.1:$HTTPS_PORT -servername $DOMAIN -showcerts 2>/dev/null | grep -c 'BEGIN CERTIFICATE' || true; }
wait_for() { local what=$1; shift; for _ in $(seq 1 120); do "$@" >/dev/null 2>&1 && return 0; sleep 0.5; done; bad "timed out: $what"; docker logs "$APP" 2>&1 | tail -n 20; return 1; }
cache=$WORK/storage/thruster

# 1, 2: the order at boot and the files.
if wait_for "the certificate file" test -s "$cache/$DOMAIN"; then
  [ "$(issued)" = 1 ] && ok "Pebble issued 1 certificate at boot" || bad "Pebble issued $(issued) certificates at boot"
  [ "$(stat -c %a "$cache/$DOMAIN")" = 600 ] && ok "$DOMAIN is 0600" || bad "$DOMAIN is $(stat -c %a "$cache/$DOMAIN")"
  [ -s "$cache/acme_account+key" ] && ok "acme_account+key exists" || bad "no acme_account+key"
  [ "$(stat -c %a "$cache/acme_account+key")" = 600 ] && ok "acme_account+key is 0600" || bad "acme_account+key is $(stat -c %a "$cache/acme_account+key")"
  [ "$(stat -c %a "$cache")" = 700 ] && ok "storage/thruster is 0700" || bad "storage/thruster is $(stat -c %a "$cache")"
  # 3: the chain on the wire.
  wait_for "the HTTPS listener serving" bash -c "echo | openssl s_client -connect 127.0.0.1:$HTTPS_PORT -servername $DOMAIN 2>/dev/null | grep -q 'BEGIN CERTIFICATE\|Verification'" || true
  n=$(chain); [ "$n" = 2 ] && ok "the HTTPS port sends a 2-certificate chain" || bad "the HTTPS port sends $n certificates"
  curl -sk --resolve "$DOMAIN:$HTTPS_PORT:127.0.0.1" -o /dev/null -w 'https %{http_version} %{http_code}\n' "https://$DOMAIN:$HTTPS_PORT/up"
  # 4: a restart loads the cache.
  thumb() { echo | openssl s_client -connect 127.0.0.1:$HTTPS_PORT -servername $DOMAIN 2>/dev/null | openssl x509 -noout -fingerprint -sha256; }
  before=$(thumb)
  since=$(date +%s)
  docker restart "$APP" >/dev/null
  wait_for "the restarted host" bash -c "docker logs --since $since '$APP' 2>&1 | grep -q 'Server started'" || true
  docker logs --since "$since" "$APP" 2>&1 | grep -q 'Loaded the cached certificate' && ok "the restart loaded the cached certificate" || bad "the restart did not log a cached load"
  sleep 3
  [ "$(issued)" = 1 ] && ok "still 1 order after the restart" || bad "Pebble issued $(issued) certificates after the restart"
  [ "$(thumb)" = "$before" ] && ok "same certificate after the restart" || bad "another certificate after the restart"
  n=$(chain); [ "$n" = 2 ] && ok "2-certificate chain after the restart" || bad "$n certificates after the restart"
fi
[ "$fail" = 0 ] && echo "pebble-image: PASSED" || echo "pebble-image: FAILED"
exit "$fail"
