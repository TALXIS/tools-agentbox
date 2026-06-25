#!/usr/bin/env bash
# Installs the Microsoft Power Platform CLI (pac) as a .NET global tool.
# Follows the same auto-update pattern as ghcr.io/devcontainers/features/copilot-cli.
set -e

CLI_VERSION="${VERSION:-"latest"}"

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET SDK is required. Add the dotnet feature before pac-cli."
    exit 1
fi

# Determine the user whose dotnet tool directory we should use
REMOTE_USER="${_REMOTE_USER:-"$(id -un)"}"
REMOTE_USER_HOME="${_REMOTE_USER_HOME:-"$(eval echo "~${REMOTE_USER}")"}"
DOTNET_TOOLS_PATH="${REMOTE_USER_HOME}/.dotnet/tools"

echo "Installing Power Platform CLI for user: ${REMOTE_USER}"

install_pac() {
    if [ "${CLI_VERSION}" = "latest" ]; then
        su "${REMOTE_USER}" -c "dotnet tool install --global Microsoft.PowerApps.CLI.Tool" \
            || su "${REMOTE_USER}" -c "dotnet tool update --global Microsoft.PowerApps.CLI.Tool"
    else
        su "${REMOTE_USER}" -c "dotnet tool install --global Microsoft.PowerApps.CLI.Tool --version ${CLI_VERSION}" \
            || su "${REMOTE_USER}" -c "dotnet tool update --global Microsoft.PowerApps.CLI.Tool --version ${CLI_VERSION}"
    fi
}

# Ensure dotnet tools directory is on PATH for all login shells
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

    # Also add for root-owned profile when running as root
    if ! grep -q '\.dotnet/tools' /etc/profile.d/dotnet-tools.sh 2>/dev/null; then
        echo "${path_line}" > /etc/profile.d/dotnet-tools.sh
        chmod 644 /etc/profile.d/dotnet-tools.sh
    fi
}

install_pac
ensure_path

# When version is 'latest', create a flag file so postStartCommand auto-updates
if [ "${CLI_VERSION}" = "latest" ]; then
    mkdir -p /etc/devcontainer-pac-cli
    touch /etc/devcontainer-pac-cli/auto-update
    echo "Auto-update enabled: pac will update on each container start."
fi

echo "Power Platform CLI installed: $(su "${REMOTE_USER}" -c "PATH=\"${DOTNET_TOOLS_PATH}:\$PATH\" pac --version" 2>/dev/null || echo 'installed')"
echo "Done!"
