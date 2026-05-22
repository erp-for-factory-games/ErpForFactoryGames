#!/usr/bin/env bash
# Disable password-based SSH on the LXC. Idempotent.
#
# Run AFTER `bootstrap-lxc.sh` and AFTER you've confirmed key-based auth
# works for both `root` and the application user. Once this runs, the root
# password is dead weight — anyone wanting in needs a key.
#
# We write a drop-in under /etc/ssh/sshd_config.d/ rather than editing the
# main config, so distro upgrades don't fight us. The drop-in wins because
# sshd reads the conf.d files first and picks the first directive it sees.

set -euo pipefail

if [[ "$(id -u)" -ne 0 ]]; then
  echo "ERROR: must be run as root." >&2
  exit 1
fi

DROPIN=/etc/ssh/sshd_config.d/10-erp-harden.conf

# Pre-flight: make sure root has at least one authorized key, otherwise this
# would lock the box completely.
if [[ ! -s /root/.ssh/authorized_keys ]]; then
  echo "ERROR: /root/.ssh/authorized_keys is empty. Refusing to disable password" >&2
  echo "       auth — that would lock everyone out." >&2
  exit 2
fi

cat > "${DROPIN}" <<'EOF'
# ERP for Factory Games — SSH hardening.
# Key-based auth only. Root may log in but only with a key.
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
EOF
chmod 0644 "${DROPIN}"

# Validate config before bouncing sshd — a typo here would lock us out.
sshd -t

systemctl reload ssh

echo "==> SSH hardened: ${DROPIN} in place, sshd reloaded."
echo "    Test from your dev box: ssh erp-lxc 'echo ok'"
