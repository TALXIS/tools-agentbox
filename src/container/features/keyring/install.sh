#!/usr/bin/env bash
# Installs a D-Bus session bus + gnome-keyring Secret Service, agentbox-keyring-init (which starts
# and unlocks them), and the shell hook that calls it. Nothing is started here: daemons started at
# image build time wouldn't survive into the running container anyway. See NOTES.md.
set -e

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v apt-get >/dev/null 2>&1; then
    echo "ERROR: the keyring Feature needs an apt-based image (Debian/Ubuntu)."
    exit 1
fi

FEATURE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

apt-get update
apt-get install -y --no-install-recommends \
    libsecret-1-0 libsecret-tools gnome-keyring dbus dbus-user-session
rm -rf /var/lib/apt/lists/*

install -m 755 "${FEATURE_DIR}/agentbox-keyring-init" /usr/local/bin/agentbox-keyring-init
install -m 644 "${FEATURE_DIR}/agentbox-keyring.sh" /etc/profile.d/agentbox-keyring.sh

if [ -f /etc/bash.bashrc ] && ! grep -q 'agentbox-keyring.sh' /etc/bash.bashrc; then
    echo '[ -r /etc/profile.d/agentbox-keyring.sh ] && . /etc/profile.d/agentbox-keyring.sh' >> /etc/bash.bashrc
fi

echo "Done! Keyring bootstrap installed (/usr/local/bin/agentbox-keyring-init)."
