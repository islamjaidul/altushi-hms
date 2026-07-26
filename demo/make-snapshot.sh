#!/bin/sh
# Captures the current seeded database as the golden template demo-reset.sh restores from.
# Run after seeding + verifying the demo state. Versioned with the app image (07 §2).
set -eu
PROJECT="${1:-hms}"
DB_CONTAINER="${DB_CONTAINER:-$PROJECT-db-1}"

docker exec "$DB_CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 <<'SQL'
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'hms' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS hms_golden;
CREATE DATABASE hms_golden TEMPLATE hms;
SQL
echo "golden snapshot captured (hms_golden)"
