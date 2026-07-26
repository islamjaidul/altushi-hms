#!/bin/sh
# CI gate (05 §7): the F-key registry is declared data; a screen redefining a reserved key
# (F2 new patient, F3 item search, F9 hold/recall, F10 payment) for another purpose fails.
# Screens register via hmsFkeys.register({F4:{label:...}}); this check flags any register()
# call that binds a reserved key with a non-reserved label.
set -eu
root="${1:-src}"
fail=0
for key in F2 F3 F9 F10; do
  case $key in
    F2) want="New Patient";; F3) want="Item Search";; F9) want="Hold/Recall";; F10) want="Payment";;
  esac
  hits=$(grep -rn "$key:" "$root" --include='*.cshtml' --include='*.js' 2>/dev/null \
    | grep -v 'fkeys.js' | grep "label" | grep -v "$want" || true)
  if [ -n "$hits" ]; then
    echo "Reserved $key rebound away from \"$want\":" >&2
    echo "$hits" >&2
    fail=1
  fi
done
[ "$fail" -eq 0 ] && echo "fkeys: OK"
exit "$fail"
