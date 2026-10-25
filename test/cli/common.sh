# Shared by the agentbox container tests. The package is in /nupkg, with a version above any
# release, so it's the agentbox every step installs.
set -euo pipefail

step() { echo; echo "=== $* ==="; }

# What the setup script does, as each sandbox runs it, from this checkout.
setup() { bash /repo/src/container/scripts/install-features.sh --source /nupkg; }

# What every sandbox must end with: the tools, and both harnesses set up, with any MCP server the plugin declares working.
check_ready() {
    step "prerequisite validate"
    agentbox prerequisite validate --format text
    step "validate both harnesses (MCP handshake for any declared server)"
    agentbox harness validate claude copilot --deep --format text
}
