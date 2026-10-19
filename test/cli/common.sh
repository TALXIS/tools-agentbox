# Shared by the agentbox CLI container tests (sourced). Expects the package in /nupkg (or
# AGENTBOX_NUPKG) and AGENTBOX_VERSION set; installs a bootstrap .NET SDK when there is none, since
# agentbox runs on .NET.
set -euo pipefail

step() { echo; echo "=== $* ==="; }

bootstrap_dotnet() {
    command -v dotnet >/dev/null 2>&1 && return
    local dir="$1"
    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "${dir}" >/dev/null
    export DOTNET_ROOT="${dir}" PATH="${dir}:${PATH}"
}

install_agentbox() {
    dotnet tool install --global --add-source "${AGENTBOX_NUPKG:-/nupkg}" TALXIS.AgentBox.CLI --version "${AGENTBOX_VERSION}"
    export PATH="${PATH}:${HOME}/.dotnet/tools"
}
