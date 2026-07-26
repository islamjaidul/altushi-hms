#!/bin/sh
# Runs once on first cluster init. Creates the two runtime roles (G11):
#   hms_migrator — DDL, runs EF migrations at startup
#   hms_app      — DML only; migrations REVOKE DELETE on financial/audit tables from it
set -eu

psql -v ON_ERROR_STOP=1 -U postgres -d hms <<SQL
CREATE ROLE hms_migrator LOGIN PASSWORD '${HMS_DB_MIGRATOR_PASSWORD}';
CREATE ROLE hms_app      LOGIN PASSWORD '${HMS_DB_APP_PASSWORD}';

GRANT ALL ON DATABASE hms TO hms_migrator;
GRANT CONNECT ON DATABASE hms TO hms_app;

-- Extensions the schema depends on (03-data-model.md):
--   pg_trgm       type-ahead search
--   fuzzystrmatch dmetaphone dup-warning (immutability verified by S1 test)
--   btree_gist    rate_version exclusion constraint
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
CREATE EXTENSION IF NOT EXISTS btree_gist;
SQL
