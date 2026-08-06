#!/usr/bin/env bash
# Restrict the CI deploy key to one command. Run as root ON the VM, after a git pull.
# Re-run it whenever deploy-remote.sh or hms-deploy-gate.sh changes — those now live root-owned
# on purpose, so CI can no longer push them over scp.
#
# Before: the key gets a shell in the docker group == root on this host.
# After:  the key can run one script, with one argument, matching one registry prefix.
#
# Touches no container. The stack keeps running throughout.
set -euo pipefail

U="${DEPLOY_USER:-deploy}"
REPO="${DEPLOY_REPO:-/opt/altushi-hms}"
GATE="/usr/local/bin/hms-deploy-gate"
AK="/home/$U/.ssh/authorized_keys"

[ "$(id -u)" -eq 0 ] || { echo "Run as root." >&2; exit 1; }
id "$U" >/dev/null 2>&1 || { echo "!! user $U does not exist" >&2; exit 1; }

echo "==> Installing the gate at $GATE (root-owned, $U cannot edit it)"
install -o root -g root -m 0755 "$REPO/deploy/hms-deploy-gate.sh" "$GATE"

echo "==> Making the deploy assets read-only to $U"
# This is the load-bearing half. A forced command is worthless if the account can rewrite the
# script the command runs, or the compose file that names the volumes — `compose.yml` can mount /
# into a container just as well as `docker run` can. Group keeps READ (deploy must read .env to
# bring the stack up) and loses WRITE.
chown -R root:"$U" "$REPO/deploy"
chmod -R g-w,o-rwx "$REPO/deploy"
chmod 0750 "$REPO/deploy"
chmod 0755 "$REPO/deploy/deploy-remote.sh"
find "$REPO/deploy" -type d -exec chmod 0750 {} +
[ -f "$REPO/deploy/.env" ] && chmod 0640 "$REPO/deploy/.env"

echo "==> Rewriting $AK with a forced command"
KEYLINE="$(grep -oE 'ssh-ed25519 [A-Za-z0-9+/=]+( .*)?$' "$AK" | head -1)" \
  || { echo "!! no ed25519 key found in $AK" >&2; exit 1; }
[ -n "$KEYLINE" ] || { echo "!! could not extract the key" >&2; exit 1; }

cp -a "$AK" "$AK.bak.$(date -u +%Y%m%dT%H%M%SZ)"
# no-pty is what actually denies the interactive shell; the rest close the tunnelling side doors.
printf 'command="%s",no-agent-forwarding,no-port-forwarding,no-pty,no-user-rc,no-X11-forwarding %s\n' \
  "$GATE" "$KEYLINE" > "$AK"
chown "$U:$U" "$AK"; chmod 600 "$AK"

echo "==> Result"
echo "  gate      : $(stat -c '%U:%G %a' "$GATE") $GATE"
echo "  deployer  : $(stat -c '%U:%G %a' "$REPO/deploy/deploy-remote.sh")"
echo "  compose   : $(stat -c '%U:%G %a' "$REPO/deploy/compose.yml")"
echo "  .env      : $(stat -c '%U:%G %a' "$REPO/deploy/.env" 2>/dev/null || echo 'absent')"
echo "  authorized_keys:"; sed 's/\(AAAA[A-Za-z0-9+\/=]\{12\}\)[A-Za-z0-9+\/=]*/\1.../' "$AK" | sed 's/^/    /'

echo
echo "Verifying $U can no longer get a shell or write the deployer:"
runuser -u "$U" -- test -w "$REPO/deploy/deploy-remote.sh" \
  && echo "  !! STILL WRITABLE — stop and investigate" \
  || echo "  deploy-remote.sh not writable by $U: OK"
runuser -u "$U" -- test -r "$REPO/deploy/.env" \
  && echo "  .env still readable by $U: OK (required)" \
  || echo "  !! .env unreadable — the stack will not start"
echo
echo "Done. CI must now send: ssh deploy@host '<registry-user> <image-ref>' with the token on stdin."
