#!/bin/bash
# usage: nav-smoke.sh <user> <password> <paths...>
#
# Loads each path as the named user and FAILS on a refusal.
#
# Spec 0029 F6. The previous version had no exit-code logic and treated 302 as acceptable —
# but deny-by-default in this app *is* a 302, to /denied. So a permission regression that sent
# an entire role to /denied printed a clean-looking table and exited 0, and the CI upgrade
# gate ran it across thirteen user/route batches. Following the redirect is the whole point:
# a 302 to /denied is a refusal, a 302 to /login is an unauthenticated session, and neither is
# a pass. This is the same distinction `Session.denied()` makes in _harness.py.
set -u
BASE=${BASE_URL:-http://localhost:5199}
BASE=${BASE%/}
U=$1; P=$2; shift 2
JAR=$(mktemp)
BODY=$(mktemp)
trap 'rm -f "$JAR" "$BODY"' EXIT

TOKEN=$(curl -s -c "$JAR" "$BASE/login" | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' | sed 's/.*value="//;s/"//')
LOGIN=$(curl -s -b "$JAR" -c "$JAR" -o "$BODY" -w '%{http_code} %{redirect_url}' -X POST "$BASE/login" \
  --data-urlencode "Username=$U" --data-urlencode "Password=$P" \
  --data-urlencode "__RequestVerificationToken=$TOKEN")
LOGIN_CODE=${LOGIN%% *}
echo "login($U) -> $LOGIN_CODE"
# A rejected sign-in re-renders the form with 200; a good one redirects away.
if [ "$LOGIN_CODE" != "302" ] && [ "$LOGIN_CODE" != "303" ]; then
  echo "  FAIL  could not sign in as $U — every route below would read as a refusal"
  exit 1
fi

FAILED=0
for p in "$@"; do
  read -r CODE TARGET <<EOF
$(curl -s -b "$JAR" -o "$BODY" -w '%{http_code} %{redirect_url}' "$BASE$p")
EOF
  SIZE=$(wc -c < "$BODY" | tr -d ' ')
  case "$CODE" in
    200) VERDICT="ok" ;;
    301|302|303|307|308)
      case "$TARGET" in
        */denied*) VERDICT="DENIED" ;;
        */login*)  VERDICT="NOT-SIGNED-IN" ;;
        *)         VERDICT="ok (redirect)" ;;
      esac ;;
    *) VERDICT="HTTP $CODE" ;;
  esac
  printf '  %-34s %s  %sB  %s\n' "$p" "$CODE" "$SIZE" "$VERDICT"
  case "$VERDICT" in
    ok|"ok (redirect)") ;;
    *) FAILED=$((FAILED + 1))
       [ "$CODE" != "302" ] && head -c 400 "$BODY" && echo ;;
  esac
done

if [ "$FAILED" -gt 0 ]; then
  echo "  FAIL  $FAILED of $# route(s) refused or broken for $U"
  exit 1
fi
exit 0
