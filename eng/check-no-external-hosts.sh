#!/bin/sh
# CI gate (edge 1 / ADR-0006): no external host may appear on the runtime critical path.
# Scans web assets and Razor views for http(s):// references that are not localhost/app-relative.
# Exits non-zero listing offenders.
set -eu

root="${1:-src}"

offenders=$(grep -rInE 'https?://' "$root" \
  --include='*.cshtml' --include='*.css' --include='*.js' --include='*.html' \
  2>/dev/null \
  | grep -vE 'https?://(localhost|127\.0\.0\.1)' \
  | grep -vE '^\s*//' \
  | grep -vE 'xmlns=|w3\.org|schema\.org|example\.(com|org)' || true)

if [ -n "$offenders" ]; then
  echo "External host references found on the asset path (edge 1 violation):" >&2
  echo "$offenders" >&2
  exit 1
fi
echo "no-external-hosts: OK"
