#!/bin/sh
# CI gate (05 §7 / G21): screens must not hardcode colours — everything goes through tokens.css.
# Scans Razor views and non-token CSS for hex colours.
set -eu
root="${1:-src}"

offenders=$(grep -rInE '#[0-9a-fA-F]{3,8}\b' "$root" \
  --include='*.cshtml' --include='*.css' 2>/dev/null \
  | grep -v 'tokens.css' \
  | grep -vE '&#' || true)

if [ -n "$offenders" ]; then
  echo "Hardcoded colours found (must use tokens.css custom properties, 05 §1):" >&2
  echo "$offenders" >&2
  exit 1
fi
echo "ui-tokens: OK"
