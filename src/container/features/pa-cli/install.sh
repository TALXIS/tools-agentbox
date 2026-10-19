#!/usr/bin/env bash
# Installs the Microsoft Power Apps CLI (pa) from npm into its own prefix, behind a wrapper in
# /usr/local/bin that starts the Secret Service keyring (the keyring Feature) first. pa hard-codes
# usePlaintextFileOnLinux: false, so without an unlocked keyring every command fails with
# "GnomeKeyringError: Couldn't access platform storage: AccessDenied".
#
# The npm package goes into PREFIX rather than npm's global prefix so the wrapper is the only `pa`
# on PATH: the node Feature puts nvm's bin dir ahead of /usr/local/bin, which would otherwise skip
# the wrapper and its keyring init.
set -e

CLI_VERSION="${VERSION:-"latest"}"
PACKAGE="@microsoft/power-apps-cli"
PREFIX="/usr/local/lib/power-apps-cli"

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
    echo "ERROR: Node.js 22 or newer is required. Add the node Feature before pa-cli."
    exit 1
fi

node_major="$(node -p 'process.versions.node.split(".")[0]')"
if [ "${node_major}" -lt 22 ]; then
    echo "ERROR: pa needs Node.js 22 or newer, found $(node --version)."
    exit 1
fi

REMOTE_USER="${_REMOTE_USER:-"$(id -un)"}"

echo "Installing ${PACKAGE}@${CLI_VERSION} into ${PREFIX}"
mkdir -p "${PREFIX}"
npm install -g --prefix "${PREFIX}" "${PACKAGE}@${CLI_VERSION}"
# `agentbox prerequisite update` runs as the remote user, not root.
chown -R "${REMOTE_USER}" "${PREFIX}" 2>/dev/null || true

cat > /usr/local/bin/pa <<WRAPPER
#!/bin/bash
# Installed by the pa-cli devcontainer Feature (TALXIS/tools-agentbox).
command -v agentbox-keyring-init >/dev/null 2>&1 && eval "\$(agentbox-keyring-init)"
exec "${PREFIX}/bin/pa" "\$@"
WRAPPER
chmod 755 /usr/local/bin/pa

if [ "${CLI_VERSION}" = "latest" ]; then
    mkdir -p /etc/devcontainer-pa-cli
    touch /etc/devcontainer-pa-cli/auto-update
else
    rm -f /etc/devcontainer-pa-cli/auto-update
fi

echo "Power Apps CLI installed: $(node -p "require('${PREFIX}/lib/node_modules/${PACKAGE}/package.json').version" 2>/dev/null || echo 'installed')"
echo "Done!"
