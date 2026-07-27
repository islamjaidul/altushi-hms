#!/bin/bash
# Spec 0023 — measure what the memory budget only ever estimated.
#
# `06-deployment.md` §2 has carried "all figures are estimates until measured against real
# containers" since spec 0003, and the phase-2 build plan hangs an abort criterion off the
# number (sustained RSS > 2.2 GB on the VM profile stops the next module). This script produces
# the measurement that criterion needs.
#
# It measures against a database with §14-shaped history in it — a figure measured on an empty
# database says nothing about a hospital's second year.
#
# usage:
#   eng/verify/measure-rss.sh                    local: app via dotnet run, db in hms-dev-db
#   MODE=vm eng/verify/measure-rss.sh            on the VM: sample `docker stats` instead
#   DB=hms_history PORT=5199 ...                 overrides
set -eu
cd "$(dirname "$0")/../.."
command -v dotnet >/dev/null 2>&1 || export PATH="$HOME/.dotnet:$PATH"

MODE="${MODE:-local}"
DB="${DB:-hms_history}"
PORT="${PORT:-5199}"
PGHOST="${PGHOST:-localhost}"; PGPORT="${PGPORT:-5455}"
PGUSER="${PGUSER:-postgres}";  PGPASSWORD="${PGPASSWORD:-dev-only-super}"
export PGPASSWORD
SETTLE="${SETTLE:-45}"          # seconds of idle before the at-rest reading

say() { printf '%s\n' "$*"; }

# ---- what we are measuring against ------------------------------------------------
row_counts() {
  psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$DB" -At -F' ' <<'SQL'
SELECT 'patients', count(*) FROM reg.patient
UNION ALL SELECT 'invoices', count(*) FROM bill.invoice
UNION ALL SELECT 'charge_lines', count(*) FROM bill.charge_line
UNION ALL SELECT 'receipts', count(*) FROM bill.receipt
UNION ALL SELECT 'test_orders', count(*) FROM diag.test_order
UNION ALL SELECT 'results', count(*) FROM lis.result
UNION ALL SELECT 'stock_moves', count(*) FROM pharm.stock_move
UNION ALL SELECT 'admissions', count(*) FROM ipd.admission
UNION ALL SELECT 'audit_events', count(*) FROM kernel.audit_event
SQL
}

db_size() {
  psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$DB" -At \
    -c "SELECT pg_size_pretty(pg_database_size('$DB'))"
}

say "== measuring against $DB ($(db_size))"
row_counts | sed 's/^/   /'

# ---- boot the app over it ----------------------------------------------------------
if [ "$MODE" = local ]; then
  # Run the built DLL under `exec`, so APP_PID is the server itself: `dotnet run` forks a
  # child, and sampling the launcher's RSS would measure the wrong process entirely.
  dotnet build src/Hms.Web/Hms.Web.csproj -v q --nologo >/dev/null
  DLL=$(ls src/Hms.Web/bin/*/net*/Hms.Web.dll | head -1)
  if curl -s -o /dev/null -m 2 "http://localhost:$PORT/login"; then
    say "port $PORT is already serving something — refusing to measure the wrong instance"; exit 1
  fi
  APP_LOG=$(mktemp)
  ( cd src/Hms.Web && exec env \
    ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://localhost:$PORT" \
    Seed__DevUsers=true \
    ConnectionStrings__Hms="Host=$PGHOST;Port=$PGPORT;Database=$DB;Username=$PGUSER;Password=$PGPASSWORD" \
    dotnet "../../$DLL" >"$APP_LOG" 2>&1 ) &
  APP_PID=$!
  trap 'kill $APP_PID 2>/dev/null || true' EXIT

  for i in $(seq 1 60); do
    curl -s -o /dev/null -w '%{http_code}' "http://localhost:$PORT/login" | grep -q 200 && break
    sleep 2
    [ "$i" = 60 ] && { say "app did not come up:"; tail -30 "$APP_LOG"; exit 1; }
  done

  # `dotnet run` forks the real server; find the child that owns the port.
  sample_app() { ps -o rss= -p "$APP_PID" 2>/dev/null | tr -d ' '; }            # KB
  sample_db()  { docker stats --no-stream --format '{{.MemUsage}}' hms-dev-db 2>/dev/null \
                   | awk '{print $1}' | sed 's/MiB//' | awk '{printf "%d", $1*1024}'; }
else
  sample_app() { docker stats --no-stream --format '{{.MemUsage}}' hms-app-1 \
                   | awk '{print $1}' | sed 's/MiB//' | awk '{printf "%d", $1*1024}'; }
  sample_db()  { docker stats --no-stream --format '{{.MemUsage}}' hms-db-1 \
                   | awk '{print $1}' | sed 's/MiB//' | awk '{printf "%d", $1*1024}'; }
fi

mb() { awk -v k="$1" 'BEGIN { printf "%.0f", k/1024 }'; }

# ---- at rest -----------------------------------------------------------------------
say "== settling for ${SETTLE}s, then reading at rest"
sleep "$SETTLE"
REST_APP=$(sample_app); REST_DB=$(sample_db)
say "   app $(mb "$REST_APP") MB · postgres $(mb "${REST_DB:-0}") MB"

# ---- page timings over the loaded database ----------------------------------------
say "== page timings over the loaded database (median of 5 per page)"
python3 eng/verify/page-timings.py "http://localhost:$PORT"

# ---- under the Playwright suite ----------------------------------------------------
PEAK_APP=$REST_APP; PEAK_DB=${REST_DB:-0}
if [ "${SKIP_UI:-0}" != 1 ] && [ -d eng/verify/ui ]; then
  say "== sampling under the Playwright suite (peak of 2s samples)"
  ( cd eng/verify/ui && BASE_URL="http://localhost:$PORT" npx playwright test >/dev/null 2>&1 ) &
  UI_PID=$!
  while kill -0 $UI_PID 2>/dev/null; do
    a=$(sample_app); d=$(sample_db)
    [ -n "${a:-}" ] && [ "$a" -gt "$PEAK_APP" ] && PEAK_APP=$a
    [ -n "${d:-}" ] && [ "$d" -gt "$PEAK_DB" ] && PEAK_DB=$d
    sleep 2
  done
  say "   peak app $(mb "$PEAK_APP") MB · peak postgres $(mb "$PEAK_DB") MB"
fi

TOTAL=$(( PEAK_APP + PEAK_DB ))
say ""
say "== MEASURED ($(date +%Y-%m-%d), mode=$MODE, db=$DB)"
say "   app       at rest $(mb "$REST_APP") MB   peak $(mb "$PEAK_APP") MB"
say "   postgres  at rest $(mb "${REST_DB:-0}") MB   peak $(mb "$PEAK_DB") MB"
say "   app+db peak total $(mb "$TOTAL") MB"
say ""
# The abort criterion of 11-build-plan-phase2.md §2.9 is stated against the VM's total RSS.
if [ "$(mb "$TOTAL")" -gt 2200 ]; then
  say "ABORT CRITERION HIT — peak $(mb "$TOTAL") MB exceeds 2200 MB. Consolidate before the next module."
  exit 2
fi
say "abort criterion clear: peak $(mb "$TOTAL") MB is under the 2200 MB stop line."
