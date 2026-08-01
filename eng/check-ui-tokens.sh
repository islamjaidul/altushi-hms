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

# Shipped CSS must actually reach the browser. Without asp-append-version the URL never changes,
# so a cached copy survives a deploy and a UI fix looks like it did not happen — which is exactly
# how a corrected login page kept rendering in its old styling after the image was rebuilt.
stale=$(grep -rIn '_content/Hms\.Shell/\(css\|js\)/' "$root" --include='*.cshtml' 2>/dev/null \
  | grep -v 'asp-append-version' || true)

if [ -n "$stale" ]; then
  echo "Static asset references without asp-append-version (cached copies survive a deploy):" >&2
  echo "$stale" >&2
  exit 1
fi

echo "ui-tokens: OK"
