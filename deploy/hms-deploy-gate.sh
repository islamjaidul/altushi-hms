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
ALLOWED_PREFIX="ghcr.io/islamjaidul/altushi-hms/app:"
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

# Our registry, our repository, and a tag that looks like a commit sha. A deploy is only ever a
# commit that passed the gate — not `:latest`, which is a moving target, and not someone else's
# image, which is how a leaked key would run arbitrary code as root via the docker socket.
case "$IMAGE" in
  "$ALLOWED_PREFIX"*) ;;
  *) deny "image must start with $ALLOWED_PREFIX";;
esac
TAG="${IMAGE#"$ALLOWED_PREFIX"}"
case "$TAG" in
  [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]*) ;;
  *) deny "tag must be a commit sha (got '$TAG')";;
esac
[ "${#TAG}" -eq 40 ] || deny "tag must be a 40-character commit sha"

[ -x "$DEPLOYER" ] || deny "deployer missing at $DEPLOYER"
[ ! -w "$DEPLOYER" ] || echo "WARNING: $DEPLOYER is writable by $(id -un) — re-run vm-harden-deploy-key.sh" >&2

echo "gate: accepted $IMAGE"
docker login "$REGISTRY" -u "$REG_USER" --password-stdin >/dev/null \
  || deny "registry login failed"

exec bash "$DEPLOYER" "$IMAGE"
