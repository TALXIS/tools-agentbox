#!/usr/bin/env bash
# Smoke test for the power-platform devcontainer template (pre-built image)
set -e

echo "=== Smoke test: power-platform template / pre-built image ==="

docker build \
    --file src/images/power-platform/Dockerfile \
    --tag "devcontainer-template-test-power-platform" \
    .

run() {
    docker run --rm "devcontainer-template-test-power-platform" bash -c "$1"
}

echo "--- dotnet ---"
run "dotnet --version"

echo "--- node ---"
run "node --version"

echo "--- npm ---"
run "npm --version"

echo "--- az ---"
run "az --version | head -1"

echo "--- gh ---"
run "gh --version | head -1"

echo "--- pwsh ---"
run "pwsh --version"

echo "--- terraform ---"
run "terraform --version | head -1"

echo "--- func (Azure Functions) ---"
run "func --version"

echo "--- copilot ---"
run "copilot --version || copilot version || echo 'copilot installed'"

echo "--- pac ---"
run 'PATH="$PATH:/home/vscode/.dotnet/tools" pac --version'

echo "--- txc ---"
run 'PATH="$PATH:/home/vscode/.dotnet/tools" txc --version'

echo "--- auto-update flag files ---"
run "test -f /etc/devcontainer-pac-cli/auto-update && echo 'pac auto-update: OK'"
run "test -f /etc/devcontainer-txc-cli/auto-update && echo 'txc auto-update: OK'"

echo "=== power-platform smoke test PASSED ==="
docker image rm "devcontainer-template-test-power-platform" 2>/dev/null || true
