#!/bin/bash
# usage: smoke.sh <user> <password> <paths...>
set -u
BASE=http://localhost:5199
U=$1; P=$2; shift 2
JAR=$(mktemp)
TOKEN=$(curl -s -c "$JAR" "$BASE/login" | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' | sed 's/.*value="//;s/"//')
CODE=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' -X POST "$BASE/login" \
  --data-urlencode "Username=$U" --data-urlencode "Password=$P" \
  --data-urlencode "__RequestVerificationToken=$TOKEN")
echo "login($U) -> $CODE"
for p in "$@"; do
  OUT=$(curl -s -b "$JAR" -o /tmp/body.$$ -w '%{http_code}' "$BASE$p")
  SIZE=$(wc -c < /tmp/body.$$ | tr -d ' ')
  printf '  %-34s %s  %sB\n' "$p" "$OUT" "$SIZE"
  if [ "$OUT" != "200" ] && [ "$OUT" != "302" ]; then head -c 400 /tmp/body.$$; echo; fi
done
rm -f /tmp/body.$$ "$JAR"
