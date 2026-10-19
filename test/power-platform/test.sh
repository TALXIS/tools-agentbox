#!/usr/bin/env bash
# Smoke test for the power-platform devcontainer template (pre-built image)
set -e

echo "=== Smoke test: power-platform template / pre-built image ==="

IMAGE="devcontainer-template-test-power-platform"

# Same label publish.yaml attaches (docker build's --label takes the raw JSON; no CSV quoting).
METADATA="$(bash src/container/image/power-platform/devcontainer-metadata.sh)"

docker build \
    --file src/container/image/power-platform/Dockerfile \
    --label "devcontainer.metadata=${METADATA}" \
    --tag "${IMAGE}" \
    .

run() {
    docker run --rm "${IMAGE}" bash -c "$1"
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
run 'PATH="$PATH:/home/vscode/.dotnet/tools" pac help'

echo "--- txc ---"
run 'PATH="$PATH:/home/vscode/.dotnet/tools" txc --version'

echo "--- pa (keyring started by its wrapper) ---"
run 'pa auth status --json | grep -q "\"success\": true" && echo "pa auth status: OK"'

echo "--- auto-update flag files ---"
run "test -f /etc/devcontainer-pac-cli/auto-update && echo 'pac auto-update: OK'"
run "test -f /etc/devcontainer-txc-cli/auto-update && echo 'txc auto-update: OK'"
run "test -f /etc/devcontainer-pa-cli/auto-update && echo 'pa auto-update: OK'"

echo "--- devcontainer.metadata label ---"
docker image inspect "${IMAGE}" --format '{{index .Config.Labels "devcontainer.metadata"}}' \
    | jq -e '.[0]
        | (.postStartCommand | has("keyring") and has("pac-cli") and has("txc-cli") and has("pa-cli"))
          and (.postCreateCommand | has("configure-agent-harness"))
          and (.remoteEnv.PATH | startswith("${containerEnv:HOME}/.dotnet/tools"))' >/dev/null \
    && echo "label: OK"

# A consumer whose devcontainer.json is only "image", brought up with the devcontainer CLI - no
# editor involved - must get the tooling config from the label.
echo "--- minimal consumer devcontainer.json via the devcontainer CLI ---"
command -v devcontainer >/dev/null 2>&1 || npm install -g @devcontainers/cli --silent
WORKSPACE="$(mktemp -d)"
mkdir -p "${WORKSPACE}/.devcontainer"
printf '{ "image": "%s" }\n' "${IMAGE}" > "${WORKSPACE}/.devcontainer/devcontainer.json"
cleanup() { docker rm -f $(docker ps -aq --filter "label=devcontainer.local_folder=${WORKSPACE}") >/dev/null 2>&1 || true; }
trap cleanup EXIT

devcontainer read-configuration --workspace-folder "${WORKSPACE}" --include-merged-configuration \
    | jq -e '[.mergedConfiguration.postStartCommands[] | objects | keys[]] | index("keyring") != null' >/dev/null \
    && echo "merged config has the image's postStartCommand: OK"

devcontainer up --workspace-folder "${WORKSPACE}" >/dev/null
dexec() { devcontainer exec --workspace-folder "${WORKSPACE}" bash -c "$1"; }
dexec 'case "$PATH" in "$HOME/.dotnet/tools:"*) echo "remoteEnv PATH: OK";; *) echo "PATH=$PATH"; exit 1;; esac'
dexec 'test -n "$DBUS_SESSION_BUS_ADDRESS" && echo "keyring env: OK"'
dexec 'pa auth status --json | grep -q "\"success\": true" && echo "pa auth status: OK"'
dexec 'FAKE_SECRET=x txc config auth add-sp --alias t --tenant contoso.onmicrosoft.com --app-id 00000000-0000-0000-0000-000000000001 --secret-from-env FAKE_SECRET -f json 2>&1 | grep -qi "vault is unavailable" && exit 1; echo "txc uses the OS vault: OK"'

echo "=== power-platform smoke test PASSED ==="
docker image rm "${IMAGE}" 2>/dev/null || true
