#!/usr/bin/env bash
# Counts /assets/ URL occurrences on the sidebar and search pages of a signed-in session (candidate or reference).
B=$1; J=$(mktemp)
tok=$(curl -s -c $J -b $J $B/session/new | grep -o 'name="authenticity_token" value="[^"]*"' | head -1 | sed 's/.*value="//;s/"$//')
curl -s -c $J -b $J -o /dev/null -X POST --data-urlencode "authenticity_token=$tok" --data-urlencode "email_address=david@37signals.com" --data-urlencode "password=secret123456" $B/session
for p in /users/me/sidebar '/searches?q=coffee'; do
  body=$(curl -s -b $J "$B$p")
  n=$(printf '%s' "$body" | grep -o '/assets/[^"'"'"' )]*' | wc -l)
  printf '%s bytes=%s asset-urls=%s\n' "$p" "$(printf '%s' "$body" | wc -c)" "$n"
done
