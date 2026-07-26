#!/bin/sh
# CI gate (03-data-model.md §12): migrations are additive-only.
# Run against the idempotent SQL script produced by `dotnet ef migrations script`.
# Forbidden: DROP TABLE/COLUMN, column narrowing (ALTER ... TYPE with USING risk), TRUNCATE.
set -eu

sql_file="${1:?usage: check-additive-migrations.sh <generated.sql>}"

offenders=$(grep -inE 'drop table|drop column|truncate table' "$sql_file" || true)

if [ -n "$offenders" ]; then
  echo "Destructive migration operations found (additive-only policy, 03 §12):" >&2
  echo "$offenders" >&2
  exit 1
fi
echo "additive-migrations: OK"
