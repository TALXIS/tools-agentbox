#!/usr/bin/env bash
# Installs the agentbox CLI for every user: the .NET tool in /usr/local/share/agentbox, behind a
# /usr/local/bin wrapper, so non-login shells (agents, CI steps, docker run) find it too.
set -e

TOOL_PATH="/usr/local/share/agentbox"

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: the .NET 10 SDK is required. Add the dotnet Feature before agentbox."
    exit 1
fi

# Installs it, or updates an existing one.
dotnet tool update TALXIS.AgentBox.CLI --tool-path "${TOOL_PATH}"

dotnet_root="${DOTNET_ROOT:-$(dotnet --list-sdks | sed -n '1s/.*\[\(.*\)\/sdk\]$/\1/p')}"
cat > /usr/local/bin/agentbox <<WRAPPER
#!/bin/bash
export DOTNET_ROOT="${dotnet_root}"
exec "${TOOL_PATH}/agentbox" "\$@"
WRAPPER
chmod 755 /usr/local/bin/agentbox

echo "agentbox installed: $(agentbox --version)"
