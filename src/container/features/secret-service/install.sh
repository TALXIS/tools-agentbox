#!/usr/bin/env bash
# Installs a D-Bus session bus + gnome-keyring Secret Service and the agentbox-keyring-init script
# that starts and unlocks them. The image has no systemd and no desktop session, so nothing else
# would start them, and libsecret clients (txc, pac, pa) would fall back to plaintext or fail.
set -e

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

FEATURE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v apt-get >/dev/null 2>&1; then
    echo "ERROR: secret-service needs an apt-based image (Debian/Ubuntu)."
    exit 1
fi

apt-get update
apt-get install -y --no-install-recommends \
    libsecret-1-0 libsecret-tools gnome-keyring dbus dbus-user-session procps
rm -rf /var/lib/apt/lists/*

install -m 755 "${FEATURE_DIR}/agentbox-keyring-init" /usr/local/bin/agentbox-keyring-init

# Sourced by login shells, by /etc/bash.bashrc (below) for interactive non-login shells, and by the
# pa/pac/txc wrappers in /usr/local/bin for everything else (agent tool calls, `docker run ... cmd`).
# It is the one place the bus address is defined; it must match agentbox-keyring-init.
cat > /etc/profile.d/agentbox-keyring.sh <<'PROFILE'
# Set up by the secret-service devcontainer Feature (TALXIS/tools-agentbox).
if [ -x /usr/local/bin/agentbox-keyring-init ]; then
    DBUS_SESSION_BUS_ADDRESS="unix:path=/tmp/agentbox-dbus-$(id -u)/bus"
    export DBUS_SESSION_BUS_ADDRESS
    /usr/local/bin/agentbox-keyring-init >/dev/null || true
fi
PROFILE
chmod 644 /etc/profile.d/agentbox-keyring.sh

if [ -f /etc/bash.bashrc ] && ! grep -q 'agentbox-keyring.sh' /etc/bash.bashrc; then
    echo '[ -r /etc/profile.d/agentbox-keyring.sh ] && . /etc/profile.d/agentbox-keyring.sh' >> /etc/bash.bashrc
fi

echo "Done! Secret Service keyring bootstrap installed (/usr/local/bin/agentbox-keyring-init)."
