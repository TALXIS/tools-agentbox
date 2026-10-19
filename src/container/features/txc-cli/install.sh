#!/usr/bin/env bash
# Installs the TALXIS DevKit CLI (txc) as a .NET tool in /usr/local/share/txc, shared by every user,
# behind a /usr/local/bin wrapper. The wrapper sets DOTNET_ROOT, which non-login shells (agents, CI
# steps, docker run) lack, and starts the keyring (keyring Feature) so sign-ins go to the OS
# credential store.
set -e

PACKAGE="TALXIS.CLI"
TOOL_PATH="/usr/local/share/txc"
CLI_VERSION="${VERSION:-"latest"}"

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK is required. Add the dotnet Feature before txc-cli."
    exit 1
fi

# Installs it, or moves an existing install to the asked-for version.
if [ "${CLI_VERSION}" = "latest" ]; then
    dotnet tool update "${PACKAGE}" --tool-path "${TOOL_PATH}"
else
    dotnet tool update "${PACKAGE}" --tool-path "${TOOL_PATH}" --version "${CLI_VERSION}" --allow-downgrade
fi

dotnet_root="${DOTNET_ROOT:-$(dotnet --list-sdks | sed -n '1s/.*\[\(.*\)\/sdk\]$/\1/p')}"
cat > /usr/local/bin/txc <<WRAPPER
#!/bin/bash
export DOTNET_ROOT="${dotnet_root}"
command -v agentbox-keyring-init >/dev/null 2>&1 && eval "\$(agentbox-keyring-init)"
exec "${TOOL_PATH}/txc" "\$@"
WRAPPER
chmod 755 /usr/local/bin/txc

# Marks it as following the latest release, for `agentbox prerequisite update`. A pin removes it.
if [ "${CLI_VERSION}" = "latest" ]; then
    mkdir -p /etc/devcontainer-txc-cli
    touch /etc/devcontainer-txc-cli/auto-update
else
    rm -f /etc/devcontainer-txc-cli/auto-update
fi

dotnet tool list --tool-path "${TOOL_PATH}"
