#!/usr/bin/env bash
# The ONLY thing the CI key can execute. Installed root-owned at /usr/local/bin/hms-deploy-gate
# by vm-harden-deploy-key.sh and named as a forced command in deploy's authorized_keys, so a
# leaked key cannot open a shell (spec 0053 follow-up).
#
# Why this matters: `deploy` is in the docker group, and the docker group can start a container
# that mounts / — it is root on the host in all but name. Without a forced command, the key is
# root. With one, the key can do exactly one thing: deploy an image from our own registry.
#
# Protocol — the client sends two whitespace-separated tokens as the SSH command:
#     <registry-user> <image-ref>
# and the registry token on stdin. Everything else is refused.
#
set -euo pipefail

REGISTRY="ghcr.io"
REPO_BASE="ghcr.io/islamjaidul/altushi-hms"
DEPLOYER="/opt/altushi-hms/deploy/deploy-remote.sh"

deny() { echo "refused: $1" >&2; exit 1; }

# A herestring, NOT stdin — stdin carries the registry token and must reach docker login unread.
read -r REG_USER IMAGE EXTRA <<<"${SSH_ORIGINAL_COMMAND:-}"

[ -n "${EXTRA:-}" ] && deny "expected exactly two arguments, got more"
[ -n "${REG_USER:-}" ] || deny "no registry user"
[ -n "${IMAGE:-}" ]    || deny "no image"

# Whitelist the character set before anything else looks at these. Nothing here is ever passed to
# a shell, but a ref containing $(...) or a newline has no legitimate reason to exist.
case "$REG_USER" in *[!A-Za-z0-9_-]*) deny "registry user has illegal characters";; esac
case "$IMAGE"    in *[!A-Za-z0-9./:_-]*) deny "image ref has illegal characters";; esac

# Our registry, one of our two SKUs, and a tag that looks like a commit sha. A deploy is only ever
# a commit that passed the gate — not `:latest`, which is a moving target, and not someone else's
# image, which is how a leaked key would run arbitrary code as root via the docker socket.
#
# The repository name selects the stack. Nothing about which compose files to touch comes off the
# wire; the caller picks a product, not a command.
case "$IMAGE" in
  "$REPO_BASE/app:"*)
    TAG="${IMAGE#"$REPO_BASE/app:"}"
    export HMS_COMPOSE_FILES="compose.yml compose.vm.yml"
    export HMS_HEALTH_URL="http://127.0.0.1:8090/health"
    export HMS_DB_NAME="hms"
    ;;
  "$REPO_BASE/hrm:"*)
    TAG="${IMAGE#"$REPO_BASE/hrm:"}"
    export HMS_COMPOSE_FILES="compose.hrm.yml compose.hrm.vm.yml"
    export HMS_HEALTH_URL="http://127.0.0.1:8091/health"
    # This SKU has no database or backup container of its own on the shared box — it uses the
    # ERP's Postgres against a separate `hrm` database (ADR-0025, compose.hrm.vm.yml), and rides
    # the ERP's backup volume. Before this, nothing dumped `hrm` at all.
    export HMS_DB_CONTAINER="hms-db-1"
    export HMS_DB_NAME="hrm"
    export HMS_BACKUP_CONTAINER="hms-backup-1"
    ;;
  *) deny "image must be $REPO_BASE/{app,hrm}:<sha>";;
esac
case "$TAG" in
  [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]*) ;;
  *) deny "tag must be a commit sha (got '$TAG')";;
esac
[ "${#TAG}" -eq 40 ] || deny "tag must be a 40-character commit sha"

echo "gate: accepted $IMAGE"

# Bring the deploy scripts and compose files up to date with `main` before using them. One
# root-owned command through a pinned sudoers rule — no arguments, so there is nothing to steer.
# Without this, every change to the deploy path needed a human at a root shell on the VM, which is
# a step in a per-release loop and therefore a step that eventually gets skipped.
if sudo -n /usr/local/bin/hms-deploy-sync 2>&1 | sed 's/^/  /'; then
  :
else
  echo "  WARNING: sync unavailable — deploying with the scripts already on this host" >&2
fi

[ -x "$DEPLOYER" ] || deny "deployer missing at $DEPLOYER"
# Spelled as an `if`, not `[ ... ] && echo`: that form returns 1 when the condition is false, and
# under `set -e` the gate would abort silently in exactly the case where the file IS locked down
# correctly. An earlier edit also had the sense inverted, so it warned whenever things were fine —
# a warning that fires on the healthy path is worse than none, because it trains you to ignore it.
if [ -w "$DEPLOYER" ]; then
  echo "WARNING: $DEPLOYER is writable by $(id -un) — the forced command is not protecting much" >&2
fi
docker login "$REGISTRY" -u "$REG_USER" --password-stdin >/dev/null \
  || deny "registry login failed"

exec bash "$DEPLOYER" "$IMAGE"
