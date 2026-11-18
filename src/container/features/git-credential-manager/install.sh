#!/usr/bin/env bash
# Installs Git Credential Manager (GCM) as a .NET tool in /usr/local/share/git-credential-manager,
# shared by every user, behind a /usr/local/bin wrapper, and makes it Git's credential helper for
# every user (/etc/gitconfig) with the Secret Service keyring (keyring Feature) as its store.
# The wrapper sets DOTNET_ROOT, which the shells Git runs helpers from often lack, and starts the
# keyring first. See NOTES.md.
set -e

PACKAGE="git-credential-manager"
TOOL_PATH="/usr/local/share/git-credential-manager"
WRAPPER="/usr/local/bin/git-credential-manager"
CLI_VERSION="${VERSION:-"latest"}"

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK is required. Add the dotnet Feature before git-credential-manager."
    exit 1
fi

if ! command -v git >/dev/null 2>&1; then
    echo "ERROR: Git is required. Add the git Feature before git-credential-manager."
    exit 1
fi

# Installs it, or moves an existing install to the asked-for version.
if [ "${CLI_VERSION}" = "latest" ]; then
    dotnet tool update "${PACKAGE}" --tool-path "${TOOL_PATH}"
else
    dotnet tool update "${PACKAGE}" --tool-path "${TOOL_PATH}" --version "${CLI_VERSION}" --allow-downgrade
fi

# GCM pins the exact .NET patch it was built with (e.g. 10.0.14), which an image with an older patch
# of the same major can't start; patches are compatible, so ask only for <major>.<minor>.0.
find "${TOOL_PATH}/.store" -name 'git-credential-manager.runtimeconfig.json' -exec \
    sed -i -E 's/("version": *"[0-9]+\.[0-9]+)\.[0-9]+"/\1.0"/' {} +

# Roll forward so it still starts where only a newer .NET major than GCM targets is installed.
dotnet_root="${DOTNET_ROOT:-$(dotnet --list-sdks | sed -n '1s/.*\[\(.*\)\/sdk\]$/\1/p')}"
cat > "${WRAPPER}" <<WRAPPER
#!/bin/bash
export DOTNET_ROOT="${dotnet_root}"
export DOTNET_ROLL_FORWARD="\${DOTNET_ROLL_FORWARD:-Major}"
command -v agentbox-keyring-init >/dev/null 2>&1 && eval "\$(agentbox-keyring-init)"
exec "${TOOL_PATH}/git-credential-manager" "\$@"
WRAPPER
chmod 755 "${WRAPPER}"

# configure writes the real binary's path as the helper, which would skip the wrapper's keyring
# start; point that entry at the wrapper, keeping the empty reset entry configure adds before it.
"${WRAPPER}" configure --system
git config --system --replace-all credential.helper "${WRAPPER}" "${TOOL_PATH}"
git config --system credential.credentialStore secretservice

# Marks it as following the latest release, for `agentbox prerequisite update`. A pin removes it.
if [ "${CLI_VERSION}" = "latest" ]; then
    mkdir -p /etc/devcontainer-git-credential-manager
    touch /etc/devcontainer-git-credential-manager/auto-update
else
    rm -f /etc/devcontainer-git-credential-manager/auto-update
fi

dotnet tool list --tool-path "${TOOL_PATH}"
git config --system --get-regexp '^credential\.'
