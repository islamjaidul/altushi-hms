#!/usr/bin/env bash
# Proves deploy/deploy-remote.sh actually rolls back (spec 0053 AC5, §8 N6).
#
# The rollback path is the one thing in the deploy that only ever runs on the worst day. Until
# this drill existed it had never executed: the code was reviewed, the happy path proved the
# health probe worked, and that was all. A recovery mechanism nobody has run is a hypothesis.
#
# Runs a throwaway stack (project `hmsdrill`) against a scratch database on whatever machine you
# are on — a laptop with Docker is the point, so this never rehearses against the hospital VM.
# Touches nothing named `hms`. Prunes nothing.
#
#   bash eng/verify/deploy-rollback-drill.sh
#
set -euo pipefail

DRILL="${TMPDIR:-/tmp}/hms-rollback-drill.$$"
PORT="${DRILL_PORT:-18090}"
REGPORT="${DRILL_REGISTRY_PORT:-15000}"
PROJECT=hmsdrill
REG="127.0.0.1:$REGPORT"
GOOD="$REG/hms-drill:good"
BAD="$REG/hms-drill:bad"
pass=0; fail=0

cleanup() {
  docker compose -p "$PROJECT" -f "$DRILL/compose.yml" -f "$DRILL/compose.vm.yml" down -v >/dev/null 2>&1 || true
  docker rm -f "$PROJECT-registry" >/dev/null 2>&1 || true
  docker rmi -f "$GOOD" "$BAD" >/dev/null 2>&1 || true
  rm -rf "$DRILL"
}
trap cleanup EXIT

ok()  { echo "  PASS  $1"; pass=$((pass+1)); }
bad() { echo "  FAIL  $1"; fail=$((fail+1)); }

mkdir -p "$DRILL"
cp "$(dirname "$0")/../../deploy/deploy-remote.sh" "$DRILL/deploy-remote.sh"

# ---------------------------------------------------------------- two app images
# good: serves {"status":"ok"} on :8080/health, exactly what the probe greps for.
# bad:  no listener at all — the shape of a container that boots and dies on a bad migration.
cat > "$DRILL/good.Dockerfile" <<'EOF'
FROM busybox:1.36
RUN mkdir -p /www && printf '{"status":"ok"}' > /www/health
CMD ["httpd","-f","-p","8080","-h","/www"]
EOF
cat > "$DRILL/bad.Dockerfile" <<'EOF'
FROM busybox:1.36
CMD ["sh","-c","echo 'startup failed: simulated bad migration' >&2; sleep 3600"]
EOF

# ---------------------------------------------------------------- the drill stack
# Same service names and shape the real deploy addresses: app / db / backup.
cat > "$DRILL/compose.yml" <<EOF
name: $PROJECT
services:
  app:
    image: "\${HMS_APP_IMAGE:-$GOOD}"
    restart: "no"
    depends_on:
      db:
        condition: service_healthy
  db:
    image: postgres:17.5-alpine
    environment:
      POSTGRES_DB: hms
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: drill-only
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d hms"]
      interval: 3s
      timeout: 3s
      retries: 15
  backup:
    image: busybox:1.36
    command: ["sh","-c","mkdir -p /var/hms/backups && sleep 3600"]
EOF
cat > "$DRILL/compose.vm.yml" <<EOF
services:
  app:
    ports:
      - "127.0.0.1:$PORT:8080"
EOF

echo "==> Starting a throwaway registry on $REG"
# deploy-remote.sh pulls, always — that is the production contract and the drill must not weaken
# it. So the drill needs a real registry to pull from; local-only tags would make `docker pull`
# fail and every rollback assertion below would pass for the wrong reason.
docker rm -f "$PROJECT-registry" >/dev/null 2>&1 || true
docker run -d --name "$PROJECT-registry" -p "127.0.0.1:$REGPORT:5000" registry:2 >/dev/null
for _ in $(seq 1 30); do
  curl -fsS "http://$REG/v2/" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS "http://$REG/v2/" >/dev/null 2>&1 || { echo "registry did not come up"; exit 1; }

echo "==> Building and pushing drill images"
docker build -q -t "$GOOD" -f "$DRILL/good.Dockerfile" "$DRILL" >/dev/null
docker build -q -t "$BAD"  -f "$DRILL/bad.Dockerfile"  "$DRILL" >/dev/null
docker push -q "$GOOD" >/dev/null
docker push -q "$BAD"  >/dev/null

echo "==> Bringing up the drill stack"
docker compose -p "$PROJECT" -f "$DRILL/compose.yml" -f "$DRILL/compose.vm.yml" up -d >/dev/null

export HMS_DEPLOY_DIR="$DRILL" HMS_HEALTH_URL="http://127.0.0.1:$PORT/health" HMS_SKIP_PRUNE=1

echo
echo "=== 1. A healthy deploy succeeds and reports the digest ==="
if HMS_HEALTH_TIMEOUT=90 bash "$DRILL/deploy-remote.sh" "$GOOD" > "$DRILL/good.log" 2>&1; then
  ok "exit 0 on a healthy image"
else
  bad "healthy deploy failed (see below)"; sed 's/^/      /' "$DRILL/good.log"
fi
GOOD_DIGEST="$(docker inspect --format '{{.Image}}' "$(docker compose -p $PROJECT -f "$DRILL/compose.yml" -f "$DRILL/compose.vm.yml" ps -q app)")"
grep -q 'Pre-deploy backup taken' "$DRILL/good.log" && ok "pre-deploy backup taken" || bad "no backup recorded"
docker compose -p "$PROJECT" -f "$DRILL/compose.yml" -f "$DRILL/compose.vm.yml" \
  exec -T backup ls /var/hms/backups/ 2>/dev/null | grep -q '\.dump' \
  && ok "dump archived into the backup volume" || bad "dump not archived"
docker compose -p "$PROJECT" -f "$DRILL/compose.yml" -f "$DRILL/compose.vm.yml" \
  exec -T backup ls /var/hms/backups/ 2>/dev/null | grep -q '\.sha256' \
  && ok "checksum archived beside the dump" || bad "checksum missing (the bug 0053 fixed)"

echo
echo "=== 2. An unhealthy deploy rolls back to the previous image ==="
set +e
HMS_HEALTH_TIMEOUT=20 bash "$DRILL/deploy-remote.sh" "$BAD" > "$DRILL/bad.log" 2>&1
RC=$?
set -e
[ "$RC" -eq 1 ] && ok "exit 1 — deploy reported as failed (rc=$RC)" \
                || bad "expected exit 1, got $RC"
grep -q 'did not become healthy' "$DRILL/bad.log" && ok "unhealthy image detected" || bad "no health failure logged"
grep -q 'Rolling back to'        "$DRILL/bad.log" && ok "rollback attempted"      || bad "no rollback attempted"
grep -q 'Rolled back .* and healthy' "$DRILL/bad.log" && ok "rollback confirmed healthy" || bad "rollback not confirmed"

echo
echo "=== 3. The stack is actually serving again after the failed deploy ==="
sleep 2
BODY="$(curl -fsS --max-time 5 "http://127.0.0.1:$PORT/health" 2>/dev/null || true)"
[ "$BODY" = '{"status":"ok"}' ] && ok "health endpoint serving: $BODY" || bad "health endpoint down: '$BODY'"
NOW_DIGEST="$(docker inspect --format '{{.Image}}' "$(docker compose -p $PROJECT -f "$DRILL/compose.yml" -f "$DRILL/compose.vm.yml" ps -q app)")"
[ "$NOW_DIGEST" = "$GOOD_DIGEST" ] \
  && ok "running the pre-deploy digest again (${GOOD_DIGEST:0:19}…)" \
  || bad "digest is $NOW_DIGEST, expected $GOOD_DIGEST"

echo
echo "=== 4. The other services were never touched ==="
docker compose -p "$PROJECT" -f "$DRILL/compose.yml" -f "$DRILL/compose.vm.yml" ps db \
  --format '{{.Status}}' 2>/dev/null | grep -qi 'up' && ok "db still up (--no-deps held)" || bad "db was disturbed"

echo
echo "──────────────────────────────────────────────"
echo "  $pass passed, $fail failed"
[ "$fail" -eq 0 ] || { echo "  DRILL FAILED"; exit 1; }
echo "  Rollback path verified."
