#!/bin/sh
# S1 skeleton of the ADR-0013 backup duty cycle: nightly custom-format dump with manifest.
# WAL archiving + off-site push + restore drill land in S5 (spec 0009).
set -eu

while true; do
  now=$(date -u +%Y%m%d-%H%M%S)
  target="/var/hms/backups/hms-$now.dump"
  if pg_dump --format=custom --file="$target" 2>/var/hms/backups/last-error.log; then
    sha256sum "$target" > "$target.sha256"
    # keep last 7 nightly dumps (ADR-0013 on-box retention)
    ls -1t /var/hms/backups/hms-*.dump 2>/dev/null | tail -n +8 | while read -r old; do
      rm -f "$old" "$old.sha256"
    done
  fi
  sleep 86400
done
