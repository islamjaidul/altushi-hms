#!/bin/sh
# ADR-0013 restore: two modes.
#   restore.sh latest            → restore newest nightly dump into the running db (fresh-machine mode)
#   restore.sh <dumpfile> [db]   → restore a specific dump (scratch drills use db=hms_drill)
# PITR-to-timestamp uses the WAL archive + a recovery.conf flow documented in RUNBOOK.md §5.
set -eu
BACKUPS="${BACKUPS_DIR:-/var/hms/backups}"
DB="${2:-hms}"

case "${1:?usage: restore.sh latest|<dumpfile> [dbname]}" in
  latest) DUMP=$(ls -1t "$BACKUPS"/hms-*.dump | head -1) ;;
  *)      DUMP="$1" ;;
esac

echo "Restoring $DUMP into database '$DB'..."
sha256sum -c "$DUMP.sha256"
dropdb --if-exists --force "$DB"
createdb "$DB"
pg_restore --dbname="$DB" --no-owner --exit-on-error "$DUMP"
echo "Restore complete. Verify: psql -d $DB -c 'SELECT count(*) FROM kernel.audit_event;'"
