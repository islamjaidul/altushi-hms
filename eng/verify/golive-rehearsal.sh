#!/bin/bash
# Spec 0023 — drive the RUNBOOK §9 rehearsal end to end on scratch databases.
#
# Two instances, because the procedure makes two different claims:
#   A. the seed flag genuinely gates seeding  → empty database + Seed:DevUsers=false ⇒ no accounts
#   B. rotation + deactivation lock the demo out → seeded database, seeding off, then §9 step 2
#
# Never point this at production: it rotates credentials and deactivates accounts.
#
# usage: eng/verify/golive-rehearsal.sh
set -eu
cd "$(dirname "$0")/../.."
command -v dotnet >/dev/null 2>&1 || export PATH="$HOME/.dotnet:$PATH"

PGHOST="${PGHOST:-localhost}"; PGPORT="${PGPORT:-5455}"
PGUSER="${PGUSER:-postgres}";  PGPASSWORD="${PGPASSWORD:-dev-only-super}"
export PGPASSWORD
PORT="${PORT:-5198}"           # not 5199: never collide with the verification app
PSQL="psql -h $PGHOST -p $PGPORT -U $PGUSER"

echo "== build once, so each instance is the server process itself"
dotnet build src/Hms.Web/Hms.Web.csproj -v q --nologo >/dev/null
DLL=$(ls src/Hms.Web/bin/*/net*/Hms.Web.dll | head -1)

# `dotnet run` launches the server as a *child*, so killing the shell's job leaves the old
# instance holding the port — and the next boot's readiness check passes against the wrong
# database. (That is exactly how a previous run reported results from the wrong app.) Running
# the built DLL under `exec` makes APP_PID the server, so stopping it really stops it.
boot() {   # db seed_flag  → starts the app, waits for /login, sets APP_PID
  if curl -s -o /dev/null -m 2 "http://localhost:$PORT/login"; then
    echo "port $PORT is already serving something — refusing to boot over it"; exit 1
  fi
  APP_LOG=$(mktemp)
  ( cd src/Hms.Web && exec env \
    ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://localhost:$PORT" \
    Seed__DevUsers="$2" \
    ConnectionStrings__Hms="Host=$PGHOST;Port=$PGPORT;Database=$1;Username=$PGUSER;Password=$PGPASSWORD" \
    dotnet "../../$DLL" >"$APP_LOG" 2>&1 ) &
  APP_PID=$!
  for i in $(seq 1 60); do
    curl -s -o /dev/null -w '%{http_code}' "http://localhost:$PORT/login" | grep -q 200 && return 0
    sleep 2
  done
  echo "app did not come up (db=$1 seed=$2):"; tail -30 "$APP_LOG"; exit 1
}

stop() {
  [ -n "${APP_PID:-}" ] || return 0
  kill "$APP_PID" 2>/dev/null || true
  wait "$APP_PID" 2>/dev/null || true
  for _ in $(seq 1 15); do
    curl -s -o /dev/null -m 1 "http://localhost:$PORT/login" || return 0
    sleep 1
  done
  echo "instance on $PORT would not stop"; exit 1
}
trap stop EXIT

echo "== A. the seed flag gates seeding (empty database, Seed:DevUsers=false)"
$PSQL -d postgres -q -c "DROP DATABASE IF EXISTS hms_golive_empty WITH (FORCE);" \
                  -c "CREATE DATABASE hms_golive_empty;"
boot hms_golive_empty false
ACCOUNTS=$($PSQL -d hms_golive_empty -At -c "SELECT count(*) FROM adm.app_user")
stop
echo "   accounts created with the flag off: $ACCOUNTS"
[ "$ACCOUNTS" = 0 ] || { echo "FAILED — seeding ran with Seed:DevUsers=false"; exit 1; }

echo "== B. rotate and deactivate on a seeded scratch database"
$PSQL -d postgres -q -c "DROP DATABASE IF EXISTS hms_golive WITH (FORCE);" \
                  -c "CREATE DATABASE hms_golive;"
boot hms_golive true          # first boot creates the demo cast
stop
boot hms_golive false         # go-live boot: seeding off, cast intact
BASE_URL="http://localhost:$PORT" python3 eng/verify/golive-rehearsal.py
stop

echo "REHEARSAL COMPLETE — RUNBOOK §9 is executable as written"
