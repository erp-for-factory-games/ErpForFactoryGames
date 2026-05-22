#!/usr/bin/env bash
# Bootstrap a fresh Ubuntu 24.04 LXC for the ERP for Factory Games stack.
#
# This script is idempotent — safe to re-run on the same LXC. When the LXC
# gets blown away and recreated, the workflow is:
#
#   1. From the dev box, scp this file to the new LXC's /root/:
#        scp deploy/bootstrap-lxc.sh erp-lxc:/root/
#   2. Run it as root:
#        ssh erp-lxc 'bash /root/bootstrap-lxc.sh'
#   3. Verify Docker works:
#        ssh erp-lxc 'sudo -u chris docker run --rm hello-world'
#   4. (Optional, but recommended once you're confident) lock down SSH:
#        ssh erp-lxc 'bash /root/harden-ssh.sh'
#
# What it does:
#   - Updates apt + installs base packages (curl, git, gnupg, ca-certificates).
#   - Installs Docker CE + compose plugin from Docker's official apt repo
#     (the distro-shipped docker.io is older and lags behind compose-v2).
#   - Creates a `chris` user in the `docker` and `sudo` groups, with passwordless
#     sudo and an ~/.ssh/authorized_keys populated from root's.
#   - Enables systemd-timesyncd for clock sync.
#
# Assumes the LXC is unprivileged with `nesting=1` (and ideally `keyctl=1`) set
# in the Proxmox container config. If Docker fails to start namespaces, that's
# almost always the reason — shut down the LXC, tick those Features in the
# Proxmox UI, start it back up, and re-run this script.

set -euo pipefail

# ---------------------------------------------------------------------------
# 0. Sanity
# ---------------------------------------------------------------------------
if [[ "$(id -u)" -ne 0 ]]; then
  echo "ERROR: must be run as root." >&2
  exit 1
fi

# Pinned for the assertions below; bump if/when we move off Noble.
EXPECTED_OS_ID="ubuntu"
EXPECTED_OS_CODENAME="noble"

. /etc/os-release
if [[ "${ID:-}" != "${EXPECTED_OS_ID}" || "${VERSION_CODENAME:-}" != "${EXPECTED_OS_CODENAME}" ]]; then
  echo "WARN: expected ${EXPECTED_OS_ID} ${EXPECTED_OS_CODENAME}; got ID=${ID:-?} CODENAME=${VERSION_CODENAME:-?}" >&2
  echo "      Continuing anyway, but the apt sources below assume noble." >&2
fi

USERNAME="${BOOTSTRAP_USER:-chris}"

echo "==> Bootstrap target: $(hostname)  user=${USERNAME}"

# ---------------------------------------------------------------------------
# 1. Base packages
# ---------------------------------------------------------------------------
echo "==> apt update + base packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq --no-install-recommends \
  ca-certificates \
  curl \
  git \
  gnupg \
  sudo \
  systemd-timesyncd

# ---------------------------------------------------------------------------
# 2. Docker CE + compose plugin (from docker.com, not the distro)
# ---------------------------------------------------------------------------
if ! command -v docker >/dev/null 2>&1; then
  echo "==> Installing Docker CE from docker.com"
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
    -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc

  cat > /etc/apt/sources.list.d/docker.list <<EOF
deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable
EOF

  apt-get update -qq
  apt-get install -y -qq \
    docker-ce \
    docker-ce-cli \
    containerd.io \
    docker-buildx-plugin \
    docker-compose-plugin
else
  echo "==> Docker already installed: $(docker --version)"
fi

# Make sure the daemon is enabled + running.
systemctl enable --now docker

# ---------------------------------------------------------------------------
# 3. Application user
# ---------------------------------------------------------------------------
if ! id -u "${USERNAME}" >/dev/null 2>&1; then
  echo "==> Creating user ${USERNAME}"
  useradd --create-home --shell /bin/bash "${USERNAME}"
else
  echo "==> User ${USERNAME} already exists"
fi

usermod -aG docker "${USERNAME}"
usermod -aG sudo "${USERNAME}"

# Passwordless sudo. Drop-in file so we don't poke the main sudoers.
cat > "/etc/sudoers.d/90-${USERNAME}" <<EOF
${USERNAME} ALL=(ALL) NOPASSWD:ALL
EOF
chmod 0440 "/etc/sudoers.d/90-${USERNAME}"

# Propagate root's authorized_keys to the user, idempotently. We don't blat
# the file because the user may have added their own keys after first boot.
USER_HOME="$(getent passwd "${USERNAME}" | cut -d: -f6)"
install -d -m 0700 -o "${USERNAME}" -g "${USERNAME}" "${USER_HOME}/.ssh"
ROOT_KEYS=/root/.ssh/authorized_keys
USER_KEYS="${USER_HOME}/.ssh/authorized_keys"
touch "${USER_KEYS}"
chown "${USERNAME}:${USERNAME}" "${USER_KEYS}"
chmod 0600 "${USER_KEYS}"

if [[ -s "${ROOT_KEYS}" ]]; then
  while IFS= read -r key; do
    # Skip empty lines and comments.
    [[ -z "${key}" || "${key}" =~ ^[[:space:]]*# ]] && continue
    if ! grep -qxF "${key}" "${USER_KEYS}"; then
      echo "${key}" >> "${USER_KEYS}"
    fi
  done < "${ROOT_KEYS}"
fi

# ---------------------------------------------------------------------------
# 4. Clock sync
# ---------------------------------------------------------------------------
echo "==> Enabling NTP"
timedatectl set-ntp true || true   # set-ntp may no-op inside an LXC; tolerated.
systemctl enable --now systemd-timesyncd || true

# ---------------------------------------------------------------------------
# 5. Smoke
# ---------------------------------------------------------------------------
echo "==> Done. Smoke check:"
docker --version
docker compose version
echo "--- groups for ${USERNAME}: $(id "${USERNAME}")"
echo "--- ssh as ${USERNAME} should now work with the same key as root."
echo
echo "NEXT: run 'docker run --rm hello-world' (as root OR sudo -u ${USERNAME})"
echo "      to confirm namespaces work. If it fails with operation-not-permitted,"
echo "      you need nesting=1 + keyctl=1 in the Proxmox LXC Features."
