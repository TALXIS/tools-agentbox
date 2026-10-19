#!/usr/bin/env bash
# Installs the TALXIS DevKit CLI (txc) as a .NET global tool.
set -e

CLI_VERSION="${VERSION:-"latest"}"

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET SDK is required. Add the dotnet feature before txc-cli."
    exit 1
fi

REMOTE_USER="${_REMOTE_USER:-"$(id -un)"}"
REMOTE_USER_HOME="${_REMOTE_USER_HOME:-"$(eval echo "~${REMOTE_USER}")"}"
DOTNET_TOOLS_PATH="${REMOTE_USER_HOME}/.dotnet/tools"

echo "Installing TALXIS DevKit CLI for user: ${REMOTE_USER}"

install_txc() {
    if [ "${CLI_VERSION}" = "latest" ]; then
        su "${REMOTE_USER}" -c "dotnet tool install --global TALXIS.CLI" \
            || su "${REMOTE_USER}" -c "dotnet tool update --global TALXIS.CLI"
    else
        su "${REMOTE_USER}" -c "dotnet tool install --global TALXIS.CLI --version ${CLI_VERSION}" \
            || su "${REMOTE_USER}" -c "dotnet tool update --global TALXIS.CLI --version ${CLI_VERSION}"
    fi
}

ensure_path() {
    local profile_files=(
        "${REMOTE_USER_HOME}/.bashrc"
        "${REMOTE_USER_HOME}/.zshrc"
        "${REMOTE_USER_HOME}/.profile"
    )
    local path_line='export PATH="$PATH:$HOME/.dotnet/tools"'

    for f in "${profile_files[@]}"; do
        if [ -f "$f" ] && ! grep -q '\.dotnet/tools' "$f"; then
            echo "${path_line}" >> "$f"
        fi
    done

    if ! grep -q '\.dotnet/tools' /etc/profile.d/dotnet-tools.sh 2>/dev/null; then
        echo "${path_line}" > /etc/profile.d/dotnet-tools.sh
        chmod 644 /etc/profile.d/dotnet-tools.sh
    fi
}

install_txc
ensure_path

# Non-login shells (agents, CI steps, docker run) never read /etc/profile.d. This wrapper puts txc
# on their PATH with DOTNET_ROOT set, and starts the keyring (keyring Feature) so sign-ins go to the
# OS credential store.
dotnet_root="${DOTNET_ROOT:-$(dotnet --list-sdks | sed -n '1s/.*\[\(.*\)\/sdk\]$/\1/p')}"
cat > /usr/local/bin/txc <<WRAPPER
#!/bin/bash
export DOTNET_ROOT="${dotnet_root}"
command -v agentbox-keyring-init >/dev/null 2>&1 && eval "\$(agentbox-keyring-init)"
exec "${DOTNET_TOOLS_PATH}/txc" "\$@"
WRAPPER
chmod 755 /usr/local/bin/txc

# Marks it as following the latest release, for `agentbox prerequisite update`. A pin removes it.
if [ "${CLI_VERSION}" = "latest" ]; then
    mkdir -p /etc/devcontainer-txc-cli
    touch /etc/devcontainer-txc-cli/auto-update
else
    rm -f /etc/devcontainer-txc-cli/auto-update
fi

echo "TALXIS DevKit CLI installed: $(su "${REMOTE_USER}" -c "PATH=\"${DOTNET_TOOLS_PATH}:\$PATH\" txc --version" 2>/dev/null || echo 'installed')"
echo "Done!"
