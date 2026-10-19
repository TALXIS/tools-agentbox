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
# Every AgentBox Feature in the manifest that has a postStartCommand must reach the label.
expected_hooks="$(
    jq -r '.features | keys[] | select(startswith("ghcr.io/talxis/tools-agentbox/"))
        | sub("^ghcr.io/talxis/tools-agentbox/"; "") | sub(":.*$"; "")' \
        src/container/templates/power-platform/.devcontainer/devcontainer.features.json \
    | while read -r id; do
        jq -e 'has("postStartCommand")' "src/container/features/${id}/devcontainer-feature.json" >/dev/null \
            && echo "${id}"
    done | jq -R . | jq -sc .)"
# `cmd && echo` would not stop the script under `set -e` when cmd fails; test it explicitly.
docker image inspect "${IMAGE}" --format '{{index .Config.Labels "devcontainer.metadata"}}' \
    | jq -e --argjson hooks "${expected_hooks}" '.[0]
        | (.postStartCommand | keys) as $keys
        | ($hooks | length > 0) and all($hooks[]; . as $h | $keys | index($h))
          and (.postCreateCommand | has("configure-agent-harness"))' >/dev/null \
    || { echo "label: FAILED" >&2; exit 1; }
echo "label: OK"

# A consumer whose devcontainer.json is only "image", brought up with the devcontainer CLI - no
# editor involved - must get the tooling config from the label.
command -v devcontainer >/dev/null 2>&1 || npm install -g @devcontainers/cli --silent
# A consumer with its own Features gets an image built on top of ${IMAGE}. CI's buildx builder
# (setup-buildx-action) runs in its own container and can't see local images, so build those with
# the Docker daemon's own builder, which can.
docker buildx use default
WORKSPACES=()
cleanup() {
    for ws in "${WORKSPACES[@]}"; do
        docker rm -f $(docker ps -aq --filter "label=devcontainer.local_folder=${ws}") >/dev/null 2>&1 || true
    done
}
trap cleanup EXIT

# Brings up a container from <devcontainer.json> with the devcontainer CLI (no editor involved)
# and checks that the image's own config reached it and its tools work.
check_consumer() {
    local name="$1" config="$2" ws
    echo "--- ${name} via the devcontainer CLI ---"
    ws="$(mktemp -d)"
    WORKSPACES+=("${ws}")
    mkdir -p "${ws}/.devcontainer"
    printf '%s\n' "${config}" > "${ws}/.devcontainer/devcontainer.json"

    devcontainer read-configuration --workspace-folder "${ws}" --include-merged-configuration \
        | jq -e '[.mergedConfiguration.postStartCommands[] | objects | keys[]] | index("keyring") != null' >/dev/null \
        || { echo "merged config lacks the image's postStartCommand" >&2; exit 1; }
    echo "merged config has the image's postStartCommand: OK"

    devcontainer up --workspace-folder "${ws}" >/dev/null
    dexec() { devcontainer exec --workspace-folder "${ws}" bash -c "$1"; }
    dexec 'for t in dotnet dnx pac txc pa; do command -v "$t" >/dev/null || { echo "$t not on PATH ($PATH)"; exit 1; }; done; echo "tools on PATH: OK"'
    dexec 'test -n "$DBUS_SESSION_BUS_ADDRESS" && echo "keyring env: OK"'
    dexec 'pa auth status --json | grep -q "\"success\": true" && echo "pa runs: OK"'
    dexec 'FAKE_SECRET=x txc config auth add-sp --alias t --tenant contoso.onmicrosoft.com --app-id 00000000-0000-0000-0000-000000000001 --secret-from-env FAKE_SECRET -f json 2>&1 | grep -qi "vault is unavailable" && exit 1; echo "txc uses the OS vault: OK"'
}

# A new project: just the image.
check_consumer "minimal devcontainer.json" "{ \"image\": \"${IMAGE}\" }"

# The workshop (TALXIS/lab), the primary consumer, with the devcontainer.json it has today, which
# still carries its own copies of the start/create hooks: the image's config must not break it.
lab_config="$(curl -fsSL https://raw.githubusercontent.com/TALXIS/lab/master/.devcontainer/devcontainer.json \
    | sed "s|\"image\": \"ghcr.io/talxis/tools-agentbox/image:latest\"|\"image\": \"${IMAGE}\"|")"
# Otherwise this would quietly test the published image instead of this build.
grep -q "\"image\": \"${IMAGE}\"" <<<"${lab_config}" \
    || { echo "TALXIS/lab's devcontainer.json no longer uses ghcr.io/talxis/tools-agentbox/image:latest" >&2; exit 1; }
check_consumer "TALXIS/lab devcontainer.json" "${lab_config}"

echo "=== power-platform smoke test PASSED ==="
docker image rm "${IMAGE}" 2>/dev/null || true
