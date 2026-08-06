#!/usr/bin/env bash
# Root-owned, installed at /usr/local/bin/hms-deploy-sync, invoked by the deploy gate through a
# single-command sudoers rule. Takes NO arguments — the sudoers entry pins the exact command line,
# so there is nothing here for a caller to steer.
#
# Why this exists: deploy/ is root-owned so a leaked CI key cannot rewrite the script it is forced
# to run, or the compose file that could mount / into a container. That is right, but it made a
# human the mechanism for propagating deploy-script changes, and a human in a loop that runs on
# every release is a step that will eventually be skipped or done wrong. This closes it.
#
# What a leaked CI key gains: the ability to make the VM pull `main` — the same `main` that is
# branch-protected and that the key cannot write to. It cannot choose the content.
# What genuinely widens: anyone who can push to `main` can change root-owned files here. That is
# the ordinary GitOps trade, and the deploy approval gate is the control that bounds it.
set -euo pipefail

REPO="/opt/altushi-hms"
GATE="/usr/local/bin/hms-deploy-gate"
U="deploy"

[ "$(id -u)" -eq 0 ] || { echo "sync: must run as root" >&2; exit 1; }
[ "$#" -eq 0 ]       || { echo "sync: takes no arguments" >&2; exit 1; }

echo "sync: fetching $REPO"
git config --global --add safe.directory "$REPO" 2>/dev/null || true
BEFORE="$(git -C "$REPO" rev-parse HEAD 2>/dev/null || echo none)"

# --ff-only: never merge, never rewrite. If the VM's checkout has diverged, that is a human
# problem and the deploy proceeds on what is already here rather than guessing.
if git -C "$REPO" pull --ff-only --quiet 2>/dev/null; then
  AFTER="$(git -C "$REPO" rev-parse HEAD)"
  if [ "$BEFORE" = "$AFTER" ]; then
    echo "sync: already at ${AFTER:0:8}"
  else
    echo "sync: ${BEFORE:0:8} -> ${AFTER:0:8}"
  fi
else
  echo "sync: pull failed or diverged — continuing on the checkout already present" >&2
fi

# Reinstall the gate and re-assert ownership. Idempotent: same result whether or not the pull
# changed anything, so this is safe to run on every single deploy.
if [ -f "$REPO/deploy/hms-deploy-gate.sh" ]; then
  install -o root -g root -m 0755 "$REPO/deploy/hms-deploy-gate.sh" "$GATE"
fi
chown -R root:"$U" "$REPO/deploy"
chmod -R g-w,o-rwx "$REPO/deploy"
find "$REPO/deploy" -type d -exec chmod 0750 {} +
find "$REPO/deploy" -name '*.sh' -exec chmod 0750 {} +
[ -f "$REPO/deploy/.env" ] && chmod 0640 "$REPO/deploy/.env"
echo "sync: deploy assets refreshed and locked"
