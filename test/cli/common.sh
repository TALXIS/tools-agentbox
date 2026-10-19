# Shared by the agentbox container tests. Expects the package in /nupkg (or AGENTBOX_NUPKG) and
# AGENTBOX_VERSION; installs a .NET SDK for agentbox itself when there is none.
set -euo pipefail

step() { echo; echo "=== $* ==="; }

bootstrap_dotnet() {
    command -v dotnet >/dev/null 2>&1 && return
    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$1" >/dev/null
    export DOTNET_ROOT="$1" PATH="$1:${PATH}"
}

install_agentbox() {
    dotnet tool install --global --add-source "${AGENTBOX_NUPKG:-/nupkg}" TALXIS.AgentBox.CLI --version "${AGENTBOX_VERSION}"
    export PATH="${PATH}:${HOME}/.dotnet/tools"
}

# What every sandbox must end with: the tools, and both harnesses set up with a working MCP server.
check_ready() {
    step "prerequisite validate"
    agentbox prerequisite validate --format text
    step "harness configure, then validate both harnesses (MCP handshake included)"
    agentbox harness configure --format text
    agentbox harness validate claude copilot --deep --format text
}
