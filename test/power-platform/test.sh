#!/usr/bin/env bash
# Smoke test for the power-platform template's pre-built image, built with this checkout's agentbox
set -e

echo "=== Smoke test: power-platform template / pre-built image ==="

IMAGE="devcontainer-template-test-power-platform"
CLI="$(mktemp -d)"
WORKSPACES=()
cleanup() {
    for ws in "${WORKSPACES[@]}"; do
        docker rm -f $(docker ps -aq --filter "label=devcontainer.local_folder=${ws}") >/dev/null 2>&1 || true
    done
    rm -rf "${CLI}"
}
trap cleanup EXIT

# Above any release, so the image installs this build wherever it would take nuget.org's.
bash src/cli/build/pack.sh 999.0.0 "${CLI}"

# Same label publish.yaml attaches (docker build's --label takes the raw JSON; no CSV quoting).
METADATA="$(bash src/container/image/power-platform/devcontainer-metadata.sh)"

# CI's buildx builder (setup-buildx-action) runs in its own container and can't see local images,
# which the consumer builds below start from; the Docker daemon's own builder can.
docker buildx use default
docker build \
    --file src/container/image/power-platform/Dockerfile \
    --build-context agentbox-cli="${CLI}" \
    --label "devcontainer.metadata=${METADATA}" \
    --tag "${IMAGE}" \
    .

echo "--- tools, from a non-login shell ---"
docker run --rm "${IMAGE}" agentbox prerequisite validate --format text

echo "--- pa (keyring started by its wrapper) ---"
docker run --rm "${IMAGE}" bash -c 'pa auth status --json | grep -q "\"success\": true" && echo "pa auth status: OK"'

# Brings up a container from <devcontainer.json> with the devcontainer CLI (no editor involved)
# and checks that the image's own config reached it and left it ready for agents.
check_consumer() {
    local name="$1" config="$2" ws
    echo "--- ${name} via the devcontainer CLI ---"
    ws="$(mktemp -d)"
    WORKSPACES+=("${ws}")
    mkdir -p "${ws}/.devcontainer"
    printf '%s\n' "${config}" > "${ws}/.devcontainer/devcontainer.json"

    devcontainer read-configuration --workspace-folder "${ws}" --include-merged-configuration \
        | jq -e '[.mergedConfiguration.postStartCommands[] | objects | keys[]] | index("agentbox") != null' >/dev/null \
        || { echo "merged config lacks the image's postStartCommand" >&2; exit 1; }
    echo "merged config has the image's postStartCommand: OK"

    devcontainer up --workspace-folder "${ws}" >/dev/null
    dexec() { devcontainer exec --workspace-folder "${ws}" bash -c "$1"; }
    dexec 'agentbox prerequisite validate --format text'
    dexec 'agentbox harness validate claude copilot --deep --format text'
    dexec 'test -n "$DBUS_SESSION_BUS_ADDRESS" && echo "keyring env: OK"'
    dexec 'FAKE_SECRET=x txc config auth add-sp --alias t --tenant contoso.onmicrosoft.com --app-id 00000000-0000-0000-0000-000000000001 --secret-from-env FAKE_SECRET -f json 2>&1 | grep -qi "vault is unavailable" && exit 1; echo "txc uses the OS vault: OK"'
}

command -v devcontainer >/dev/null 2>&1 || npm install -g @devcontainers/cli --silent

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
