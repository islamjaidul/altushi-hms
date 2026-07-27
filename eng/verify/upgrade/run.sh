#!/bin/bash
# Upgrade-path gate (ADR-0022, spec 0015): boot the CURRENT build against the PREVIOUS
# release's data and prove the product still works on it. This is the test that would have
# caught the reference-band defect (spec 0013, commit 095f552) — every local run used to
# start from a clean database, so old-shape data was never exercised.
#
# Steps: restore eng/verify/upgrade/prev-release.sql into a scratch DB → start the app over
# it (migrations + seed-upgraders run) → smoke every route over the OLD records → run the
# dirty-database-tolerant workflow script (discount-and-dues) end to end.
#
# usage: eng/verify/upgrade/run.sh              (local: docker hms-dev-db on :5455)
#   env: PGHOST/PGPORT/PGUSER/PGPASSWORD to override (CI service container)
set -eu
cd "$(dirname "$0")/../../.."
# .NET SDK is user-local on dev machines (see repo README); CI has it on PATH already.
command -v dotnet >/dev/null 2>&1 || export PATH="$HOME/.dotnet:$PATH"

DB=hms_upgrade
PGHOST="${PGHOST:-localhost}"; PGPORT="${PGPORT:-5455}"
PGUSER="${PGUSER:-postgres}";  PGPASSWORD="${PGPASSWORD:-dev-only-super}"
export PGPASSWORD
PSQL="psql -h $PGHOST -p $PGPORT -U $PGUSER"

echo "== restore previous-release fixture into $DB"
$PSQL -d postgres -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE IF EXISTS $DB WITH (FORCE);" -c "CREATE DATABASE $DB;"
$PSQL -d "$DB" -q -v ON_ERROR_STOP=1 -f eng/verify/upgrade/prev-release.sql

echo "== boot current build over it (migrations + seed upgraders run at start)"
APP_LOG=$(mktemp)
( cd src/Hms.Web && \
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 \
  ConnectionStrings__Hms="Host=$PGHOST;Port=$PGPORT;Database=$DB;Username=$PGUSER;Password=$PGPASSWORD" \
  dotnet run --no-launch-profile >"$APP_LOG" 2>&1 ) &
APP_PID=$!
trap 'kill $APP_PID 2>/dev/null || true' EXIT

for i in $(seq 1 60); do
  curl -s -o /dev/null -w '%{http_code}' http://localhost:5199/login | grep -q 200 && break
  sleep 2
  [ "$i" = 60 ] && { echo "app did not come up; log tail:"; tail -40 "$APP_LOG"; exit 1; }
done

echo "== smoke every route over the restored (old) data"
FAILED=0
smoke() {  # user pass paths...
  OUT=$(sh eng/verify/nav-smoke.sh "$@")
  echo "$OUT"
  echo "$OUT" | awk '$2 ~ /^[0-9]+$/ && $2 != 200 && $2 != 302 { exit 1 }' || FAILED=1
}
smoke jashim "Demo#1234" /registration /registration/new /registration/1/card /appointments
smoke rasel  "Demo#1234" /billing/opd /billing/dues /billing/refund /billing/reports /billing/invoice/1 /diagnostics/order
smoke ripon  "Demo#1234" /lis/board /lis/results
smoke farhana "Demo#1234" /lis/verify
smoke admin  "Demo#1234" /admin/masters /admin/users /admin/audit
smoke md     "Demo#1234" /dashboard
smoke parvin "Demo#1234" /pharmacy/pos /pharmacy/stock /pharmacy/purchase /pharmacy/reports /pharmacy/dashboard /pharmacy/indents
smoke jashim "Demo#1234" /ipd/board /ipd/admit /ipd/admissions /ipd/reports /frontdesk
smoke nasrin "Demo#1234" /ipd/indents /emr/vitals /emr/charts
smoke chowdhury "Demo#1234" /emr/queue /emr/history /emr/templates
smoke shaheen "Demo#1234" /ot/board /ot/schedule /ot/register /ot/theatres
smoke moinul "Demo#1234" /radiology/worklist
smoke rasel  "Demo#1234" /ipd/certificates
[ "$FAILED" = 0 ] || { echo "UPGRADE GATE FAILED — a route broke on old data"; exit 1; }

echo "== full money workflow on the upgraded database"
python3 eng/verify/discount-and-dues.py

echo "== pharmacy workflow on the upgraded database (spec 0016)"
python3 eng/verify/pharmacy-thread.py

echo "== ipd workflow on the upgraded database (spec 0017)"
python3 eng/verify/ipd-thread.py

echo "== whole-lifecycle seams on the upgraded database (spec 0020)"
python3 eng/verify/lifecycle-thread.py

echo "== every pharmacy feature on the upgraded database (spec 0022)"
python3 eng/verify/pharmacy-full.py

echo "== clinical record on the upgraded database (spec 0024)"
python3 eng/verify/emr-thread.py

echo "== operation theatre on the upgraded database (spec 0025)"
python3 eng/verify/ot-thread.py

echo "== radiology on the upgraded database (spec 0026)"
python3 eng/verify/radiology-thread.py

echo "== advanced edge cases on the upgraded database (specs 0020/0021)"
python3 eng/verify/edge-cases.py

echo "== front desk estimate + public surface on the upgraded database (specs 0018/0019)"
python3 eng/verify/frontdesk-check.py
for p in /public/queue /public/report-status; do
  CODE=$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:5199$p")
  echo "  anonymous $p -> $CODE"
  [ "$CODE" = 200 ] || { echo "UPGRADE GATE FAILED — public route broke"; exit 1; }
done

echo "UPGRADE GATE PASSED — current build works on previous-release data"
