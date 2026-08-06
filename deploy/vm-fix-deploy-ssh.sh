#!/usr/bin/env bash
# Diagnose (and where safe, fix) why deploy@ cannot log in with the CI key. Run as root ON the VM.
# Read-only except for the two repairs it names out loud: SELinux relabelling and mode/ownership
# on ~/.ssh. Touches no container, no sshd config, no other account.
set -uo pipefail
U="${DEPLOY_USER:-deploy}"
H="/home/$U"

echo "=== 1. account ==="
id "$U" 2>&1 || { echo "!! user $U does not exist — vm-create-deploy-user.sh did not get that far"; exit 1; }
getent passwd "$U" | awk -F: '{print "  shell:", $7, "| home:", $6}'
passwd -S "$U" 2>/dev/null | awk '{print "  password state:", $2, "(L=locked is fine, key auth is what we want)"}'

echo "=== 2. authorized_keys ==="
if [ -f "$H/.ssh/authorized_keys" ]; then
  echo "  lines: $(wc -l < "$H/.ssh/authorized_keys")"
  # Fingerprint, not the key itself, so this is safe to paste back.
  ssh-keygen -lf "$H/.ssh/authorized_keys" 2>&1 | sed 's/^/  /'
  echo "  --- expecting SHA256:6VZ0nQrmYNlgQZ5FfyOZzxLXNvxD8xY0dJ8kZ8kQ0kU-style ed25519 entry above"
else
  echo "!! $H/.ssh/authorized_keys missing"
fi

echo "=== 3. ownership and modes (sshd refuses group/world-writable) ==="
stat -c '  %n  mode=%a owner=%U:%G' "$H" "$H/.ssh" "$H/.ssh/authorized_keys" 2>&1

echo "=== 4. repairing ownership and modes ==="
chown -R "$U:$U" "$H/.ssh"
chmod 700 "$H/.ssh"; chmod 600 "$H/.ssh/authorized_keys"
chmod go-w "$H"
echo "  done"

echo "=== 5. SELinux ==="
if command -v getenforce >/dev/null 2>&1; then
  echo "  mode: $(getenforce)"
  ls -Zd "$H/.ssh" "$H/.ssh/authorized_keys" 2>&1 | sed 's/^/  /'
  if command -v restorecon >/dev/null 2>&1; then
    restorecon -R -v "$H/.ssh" 2>&1 | sed 's/^/  relabelled: /' || true
    echo "  after:"; ls -Zd "$H/.ssh" "$H/.ssh/authorized_keys" 2>&1 | sed 's/^/    /'
  fi
else
  echo "  SELinux not present"
fi

echo "=== 6. sshd policy that could block this user ==="
sshd -T 2>/dev/null | grep -iE '^(pubkeyauthentication|authorizedkeysfile|allowusers|denyusers|allowgroups|denygroups|permitrootlogin|usedns)' | sed 's/^/  /'

echo "=== 7. docker access ==="
id -nG "$U" | tr ' ' '\n' | grep -qx docker && echo "  in docker group: yes" || echo "  !! not in docker group"

echo "=== 8. deploy dir ==="
ls -ld /opt/altushi-hms/deploy 2>&1 | sed 's/^/  /'
runuser -u "$U" -- test -w /opt/altushi-hms/deploy && echo "  writable by $U: yes" || echo "  !! not writable by $U"
runuser -u "$U" -- test -r /opt/altushi-hms/deploy/.env && echo "  .env readable by $U: yes" || echo "  !! .env not readable by $U"

echo
echo "=== 9. last 15 sshd rejections (the actual reason) ==="
{ journalctl -u sshd -n 200 --no-pager 2>/dev/null || tail -200 /var/log/secure 2>/dev/null; } \
  | grep -iE "$U|authentication refused|bad ownership|invalid user" | tail -15 | sed 's/^/  /'
