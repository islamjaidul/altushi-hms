#!/bin/sh
# CI gate: every class a Razor page puts on an element must exist in the shipped stylesheets.
#
# An undefined class is not an error anywhere in the stack. The page returns 200, the markup is
# correct, and the element simply lays out as if the class were not there. That is how `.filter-row`,
# `.check` and `.inline-form` shipped on the HR screens: the search button fell to the next line at
# baseline alignment and sat across the input's bottom border. Route checks, permission checks and
# entitlement checks all passed over it, because none of them can see a layout.
#
# Usage: eng/check-css-classes.sh [src-root]
set -eu
root="${1:-src}"
css=$(find "$root" -name '*.css' -not -path '*/obj/*' -not -path '*/bin/*')

[ -n "$css" ] || { echo "check-css-classes: no stylesheets found under $root" >&2; exit 1; }

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

# Defined: class names appearing in selector position — the text of a rule up to its opening brace,
# or a line that continues a selector list. Reading the whole file instead would take `.woff2` out of
# a url() as a class name; reading only line starts would miss `table.data` and `.pill.good`, which
# is how a compound selector hides a perfectly well-defined class from a naive check.
# The <style> blocks are for the standalone pages that carry their own CSS (the public queue display
# has no sidebar to inherit from).
# shellcheck disable=SC2086
cat $css > "$tmp/all.css"
grep -rhE '^[[:space:]]+\.[A-Za-z][A-Za-z0-9_.,:>+~ -]*\{' "$root" --include='*.cshtml' 2>/dev/null \
  >> "$tmp/all.css" || true
grep -E '\{|,[[:space:]]*$' "$tmp/all.css" \
  | sed -E 's/\{.*$//' \
  | grep -oE '\.[A-Za-z][A-Za-z0-9_-]*' | sed 's/^\.//' | sort -u > "$tmp/defined"

# Names set from JavaScript or carried by the browser rather than declared as selectors. Each is
# listed because something outside CSS gives it meaning, not because it is unstyled.
cat > "$tmp/allowed" <<'EOF'
sr-only
EOF
grep -rhoE "classList\.(add|remove|toggle)\('[A-Za-z0-9_-]+'" "$root" --include='*.js' 2>/dev/null \
  | sed -E "s/.*'([^']+)'.*/\1/" >> "$tmp/allowed" || true
sort -u "$tmp/allowed" -o "$tmp/allowed"

# Used: class attributes whose value is entirely literal. Anything with an @ is computed at render
# time — a Razor ternary can put quotes inside the attribute, so parsing it here would invent tokens
# rather than find them. Those attributes are skipped whole; the literal ones are what this catches.
grep -rhoE 'class="[^"@]*"' "$root" --include='*.cshtml' \
  | sed -E 's/^class="//; s/"$//' \
  | tr ' ' '\n' \
  | grep -vE '^$' \
  | sort -u > "$tmp/used"

missing=$(comm -23 "$tmp/used" "$(sort -u "$tmp/defined" "$tmp/allowed" > "$tmp/known"; echo "$tmp/known")")

if [ -n "$missing" ]; then
  echo "Classes used in a Razor page but defined in no stylesheet." >&2
  echo "The page will render, unstyled, and nothing else will tell you:" >&2
  for c in $missing; do
    echo "  .$c" >&2
    grep -rl "class=\"[^\"]*\b$c\b" "$root" --include='*.cshtml' | sed 's/^/      /' >&2
  done
  exit 1
fi

echo "css-classes: OK"
