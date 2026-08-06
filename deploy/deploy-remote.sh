#!/usr/bin/env bash
# Runs ON the VM, invoked over SSH by .github/workflows/ci.yml (spec 0053).
#
# Swaps the ERP app container to a pre-built image from the registry and proves it healthy.
# Nothing is compiled here: the VM's 3 GB belongs to the running product (§16). If the new
# image does not come up healthy, the previously running image is put back automatically
# (§8 N6 — a maintenance action must not leave the counter down).
#
# Rollback restores the IMAGE only, never the database. Schema is additive-only (03 §12), so
# the previous app runs against the new schema; that is what makes an image-only rollback safe.
#
#   deploy-remote.sh <image-ref>
#
set -euo pipefail

IMAGE="${1:?usage: deploy-remote.sh <image-ref>}"
DEPLOY_DIR="${HMS_DEPLOY_DIR:-/opt/altushi-hms/deploy}"
HEALTH_URL="${HMS_HEALTH_URL:-http://127.0.0.1:8090/health}"
HEALTH_TIMEOUT="${HMS_HEALTH_TIMEOUT:-120}"

# The ERP stack only. compose.yml sets `name: hms`; naming both files explicitly keeps this
# off the other three products sharing this box (spec 0053, risk 2). Never a bare `down`.
COMPOSE=(docker compose -f "${DEPLOY_DIR}/compose.yml" -f "${DEPLOY_DIR}/compose.vm.yml")

log() { printf '\n=== %s\n' "$*"; }

cd "$DEPLOY_DIR"

# ---------------------------------------------------------------- 1. record the rollback point
# The digest actually running right now, not the tag we think is running — a tag can be moved.
PREVIOUS_IMAGE="$(docker inspect --format '{{.Image}}' hms-app-1 2>/dev/null \
  || docker inspect --format '{{.Image}}' "$("${COMPOSE[@]}" ps -q app 2>/dev/null)" 2>/dev/null \
  || true)"

if [ -n "$PREVIOUS_IMAGE" ]; then
  log "Currently running: $PREVIOUS_IMAGE"
else
  log "No app container running — this is a first deploy, no rollback point"
fi

# ---------------------------------------------------------------- 2. backup before we touch it
# §8 N3. Insurance only: the swap itself does not migrate destructively, but a deploy is the
# moment we most want a restore point that predates it.
log "Backing up the database before the swap"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
if "${COMPOSE[@]}" exec -T db pg_dump -U postgres -d hms --format=custom \
     > "/tmp/hms-predeploy-${STAMP}.dump" 2>/tmp/hms-predeploy-err; then
  # The checksum travels WITH the dump. Writing it to /tmp and then deleting the dump left an
  # orphaned .sha256 beside nothing, and an archived dump no one could verify — every other file
  # in that volume has its sidecar (RUNBOOK §5), and a restore point you cannot check is not one.
  ( cd /tmp && sha256sum "hms-predeploy-${STAMP}.dump" > "hms-predeploy-${STAMP}.sha256" )
  if "${COMPOSE[@]}" cp "/tmp/hms-predeploy-${STAMP}.dump" \
        "backup:/var/hms/backups/predeploy-${STAMP}.dump" 2>/dev/null; then
    "${COMPOSE[@]}" cp "/tmp/hms-predeploy-${STAMP}.sha256" \
        "backup:/var/hms/backups/predeploy-${STAMP}.sha256" 2>/dev/null || true
  else
    cp "/tmp/hms-predeploy-${STAMP}.dump"   "${DEPLOY_DIR}/predeploy-${STAMP}.dump"
    cp "/tmp/hms-predeploy-${STAMP}.sha256" "${DEPLOY_DIR}/predeploy-${STAMP}.sha256"
  fi
  cat "/tmp/hms-predeploy-${STAMP}.sha256"
  rm -f "/tmp/hms-predeploy-${STAMP}.dump" "/tmp/hms-predeploy-${STAMP}.sha256"
  log "Pre-deploy backup taken: predeploy-${STAMP}.dump (+ .sha256)"
else
  echo "!! pg_dump failed — refusing to deploy without a restore point (§8 N3)" >&2
  cat /tmp/hms-predeploy-err >&2 || true
  exit 1
fi

# ---------------------------------------------------------------- 3. pull (no build, ever)
log "Pulling $IMAGE"
docker pull "$IMAGE"

# ---------------------------------------------------------------- 4. health probe
wait_healthy() {
  local deadline=$(( SECONDS + HEALTH_TIMEOUT ))
  while [ "$SECONDS" -lt "$deadline" ]; do
    if curl -fsS --max-time 5 "$HEALTH_URL" 2>/dev/null | grep -q '"status"'; then
      return 0
    fi
    sleep 3
  done
  return 1
}

# ---------------------------------------------------------------- 5. swap
# --no-deps: db and backup keep running untouched; only the app process is replaced. The image
# is already local from step 3, so `up` cannot fall back to building it.
log "Swapping app to $IMAGE"
if HMS_APP_IMAGE="$IMAGE" "${COMPOSE[@]}" up -d --no-deps app && wait_healthy; then
  log "Healthy on $IMAGE"
  docker inspect --format 'running digest: {{.Image}}' "$("${COMPOSE[@]}" ps -q app)"
  docker image prune -f --filter "until=168h" >/dev/null 2>&1 || true
  exit 0
fi

# ---------------------------------------------------------------- 6. rollback
echo "!! New image did not become healthy within ${HEALTH_TIMEOUT}s" >&2
log "Last 50 log lines from the failed container"
"${COMPOSE[@]}" logs --tail=50 app >&2 || true

if [ -z "$PREVIOUS_IMAGE" ]; then
  echo "!! No previous image to roll back to — the stack is DOWN and needs a human" >&2
  exit 1
fi

log "Rolling back to $PREVIOUS_IMAGE"
if HMS_APP_IMAGE="$PREVIOUS_IMAGE" "${COMPOSE[@]}" up -d --no-deps app && wait_healthy; then
  echo "Rolled back to $PREVIOUS_IMAGE and healthy. The deploy is a failure; the site is up." >&2
  exit 1
fi

echo "!! ROLLBACK ALSO FAILED — stack is down, escalate (RUNBOOK §4)" >&2
exit 2
