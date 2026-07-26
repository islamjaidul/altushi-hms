#!/bin/sh
# 07 §2: one-command reset to the golden snapshot. Target < 30 s (script self-times).
#   demo/make-snapshot.sh   captures the current db as the golden template (run after seeding)
#   demo/demo-reset.sh [-p project]   restores it via Postgres template-database copy (fastest path)
set -eu
PROJECT="${2:-hms}"
DB_CONTAINER="${DB_CONTAINER:-$PROJECT-db-1}"
START=$(date +%s)

docker exec "$DB_CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 <<'SQL'
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'hms' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS hms;
CREATE DATABASE hms TEMPLATE hms_golden;
SQL

# clear queues so replayed jobs/SSE don't fire stale side effects (07 §2)
docker exec "$DB_CONTAINER" psql -U postgres -d hms -v ON_ERROR_STOP=1 \
  -c "TRUNCATE kernel.job; TRUNCATE kernel.outbox;" >/dev/null

docker restart "$PROJECT-app-1" >/dev/null

END=$(date +%s)
echo "demo-reset: done in $((END-START))s (target < 30s)"
