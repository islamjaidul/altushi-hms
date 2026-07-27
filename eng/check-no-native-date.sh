#!/bin/sh
# CI gate (ADR-0020): no native <input type="date"> anywhere — it renders the *browser's*
# locale (mm/dd/yyyy on en-US), defeating §7 U13's forgiving dd/mm entry and U9's one-grammar
# rule. Date fields use the hms-date tag helper + Hms.Kernel.Time.FlexibleDate instead.
set -eu
root="${1:-src}"

offenders=$(grep -rInE 'type="date"|type=date\b' "$root" \
  --include='*.cshtml' --include='*.html' 2>/dev/null || true)

if [ -n "$offenders" ]; then
  echo "Native date inputs found (use the hms-date tag helper, ADR-0020):" >&2
  echo "$offenders" >&2
  exit 1
fi
echo "no-native-date: OK"
