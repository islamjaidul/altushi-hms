#!/usr/bin/env bash
# ONE-TIME, run as root ON the VM (103.132.96.250). Creates the unprivileged account GitHub
# Actions deploys as (spec 0053).
#
# Why not root: the CI deploy key is held by a third party (GitHub) and is used unattended. If it
# leaks, it should buy an attacker the ability to restart the ERP app — not the whole box that
# also hosts three other products and the hospital's database.
#
#   curl -fsSL <this file> | bash -s -- 'ssh-ed25519 AAAA... github-actions-deploy@altushi-hms'
# or copy it over and:
#   bash vm-create-deploy-user.sh 'ssh-ed25519 AAAA... github-actions-deploy@altushi-hms'
#
set -euo pipefail

PUBKEY="${1:?usage: vm-create-deploy-user.sh '<ssh public key>'}"
USER_NAME="${DEPLOY_USER:-deploy}"
REPO_DIR="${DEPLOY_DIR:-/opt/altushi-hms}"

[ "$(id -u)" -eq 0 ] || { echo "Run this as root." >&2; exit 1; }

echo "==> Creating $USER_NAME"
id -u "$USER_NAME" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$USER_NAME"

echo "==> Granting docker access"
getent group docker >/dev/null || groupadd docker
usermod -aG docker "$USER_NAME"

echo "==> Installing the CI public key"
install -d -m 700 -o "$USER_NAME" -g "$USER_NAME" "/home/$USER_NAME/.ssh"
touch "/home/$USER_NAME/.ssh/authorized_keys"
grep -qxF "$PUBKEY" "/home/$USER_NAME/.ssh/authorized_keys" \
  || echo "$PUBKEY" >> "/home/$USER_NAME/.ssh/authorized_keys"
chmod 600 "/home/$USER_NAME/.ssh/authorized_keys"
chown -R "$USER_NAME:$USER_NAME" "/home/$USER_NAME/.ssh"

echo "==> Giving $USER_NAME the deploy directory"
# It needs to receive compose files and the deploy script over scp, and read deploy/.env, which
# holds the database passwords. Group-owned rather than user-owned so root keeps control.
[ -d "$REPO_DIR" ] || { echo "!! $REPO_DIR does not exist" >&2; exit 1; }
chgrp -R "$USER_NAME" "$REPO_DIR/deploy"
chmod -R g+rwX "$REPO_DIR/deploy"

echo "==> Verifying"
sudo -u "$USER_NAME" docker ps >/dev/null && echo "    docker: OK"
sudo -u "$USER_NAME" test -w "$REPO_DIR/deploy" && echo "    deploy dir writable: OK"
sudo -u "$USER_NAME" test -r "$REPO_DIR/deploy/.env" && echo "    .env readable: OK" \
  || echo "    !! $REPO_DIR/deploy/.env not readable — the stack needs its passwords"

cat <<EOF

Done. $USER_NAME can now deploy.

Confirm the host fingerprints below match what GitHub Actions was given (I scanned them over
the network; comparing them here, on the console, is what makes that scan trustworthy):
EOF
ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
ssh-keygen -lf /etc/ssh/ssh_host_rsa_key.pub
