#!/bin/bash
# Installs the devcontainer Features (https://containers.dev) listed in
# src/templates/power-platform/.devcontainer/devcontainer.features.json directly onto the host,
# for environments with no Docker daemon (Claude Code cloud) or no devcontainer.json support
# (GitHub Copilot cloud sandbox). Also registers the TALXIS/skills plugin (Skills + the `txc` MCP
# server) with whichever agent harness is calling this script.
#
# Claude Code cloud environment: paste the bootstrap command from the README's "Claude Code cloud
# environment" section into the environment's "Setup script" field at
# claude.ai/admin-settings/cloud-environments. That environment also requires:
#   - Environment variables: DOTNET_ROOT=/usr/local/dotnet/current, CLAUDE_CODE_PLUGIN_SEED_DIR=/usr/local/claude-plugin-seed
#   - Network access: Custom, defaults included, plus `cli.github.com`
#
# GitHub Copilot cloud sandbox: run via .github/workflows/copilot-setup-steps.yml.
#
# Also used by src/images/power-platform/Dockerfile to build the pre-built image, with
# AGENTBOX_HARNESS=none — the image build shouldn't register the plugin for either harness itself:
# Codespaces' own postCreateCommand does that once, at container creation, as the container's real
# user (see src/scripts/install-talxis-plugin.sh).
#
# AGENTBOX_HARNESS ("claude", "copilot", or "none"), set by the callers above, skips the plugin
# setup for whichever harness isn't relevant to that caller, so nobody pays for another harness's
# network call or risks seeing its warnings. Left unset (manual/local runs), both are attempted if
# installed.
set -uo pipefail

# $HOME may be unset in the invoking environment; every path below depends on it.
export HOME="${HOME:-$(eval echo "~$(id -un)")}"

FEATURES_MANIFEST_URL="https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/templates/power-platform/.devcontainer/devcontainer.features.json"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

export _CONTAINER_USER="$(id -un)"
export _REMOTE_USER="${_CONTAINER_USER}"
export _REMOTE_USER_HOME="${HOME}"

# npm/Node aren't present on a bare host (e.g. the Dockerfile's plain ubuntu:24.04 base) — bootstrap
# just enough to run the devcontainers CLI below. The Feature list's own pinned "node" version (if
# present) installs afterward in the resolved order and replaces this bootstrap copy.
if ! command -v npm >/dev/null 2>&1; then
    echo "--- Bootstrapping Node.js (npm not found) ---"
    apt-get update && apt-get install -y --no-install-recommends nodejs npm
fi

npm install -g @devcontainers/cli --silent

# dotnet isn't present on a bare host; install it first, since pac-cli/txc-cli depend on it.
mkdir -p "${WORKDIR}/.devcontainer"
curl -fsSL "${FEATURES_MANIFEST_URL}" -o "${WORKDIR}/features-manifest.json"
jq '{features: (.features + {"ghcr.io/devcontainers/features/dotnet:1": {"version": "10.0"}})}' \
    "${WORKDIR}/features-manifest.json" > "${WORKDIR}/.devcontainer/devcontainer.json"

echo "Resolving Feature versions and install order..."
# resolve-dependencies prints a mermaid flowchart before the JSON result; keep only the JSON.
RESOLVED="$(devcontainer features resolve-dependencies --workspace-folder "${WORKDIR}" | sed -n '/^{/,$p')"

install_feature() {
    local entry="$1" n="$2"
    local id path registry rest digest token manifest layer_digest feat_dir
    id="$(jq -r '.id' <<<"${entry}")"
    registry="${id%%/*}"
    rest="${id#*/}"
    path="${rest%@*}"
    digest="${rest##*@}"

    echo "--- Installing ${path} (${digest}) ---"

    token="$(curl -fsSL "https://${registry}/token?service=${registry}&scope=repository:${path}:pull" | jq -r .token)"
    manifest="$(curl -fsSL -H "Authorization: Bearer ${token}" \
        -H "Accept: application/vnd.oci.image.manifest.v1+json" \
        "https://${registry}/v2/${path}/manifests/${digest}")"
    layer_digest="$(jq -r '.layers[0].digest' <<<"${manifest}")"

    feat_dir="${WORKDIR}/feat-${n}"
    mkdir -p "${feat_dir}"
    curl -fsSL -H "Authorization: Bearer ${token}" "https://${registry}/v2/${path}/blobs/${layer_digest}" \
        | tar -x -C "${feat_dir}"

    local env_args=()
    while IFS=$'\t' read -r key value; do
        [ -z "${key}" ] && continue
        local var_name
        var_name="$(tr '[:lower:]' '[:upper:]' <<<"${key}" | sed -E 's/[^A-Z0-9_]/_/g; s/^[0-9_]+/_/')"
        env_args+=("${var_name}=${value}")
    done < <(jq -r '.options // {} | to_entries[] | "\(.key)\t\(.value)"' <<<"${entry}")

    ( cd "${feat_dir}" && env "${env_args[@]}" bash install.sh ) \
        || echo "WARNING: install of ${path} failed, continuing" >&2

    # Apply each Feature's declared containerEnv (e.g. dotnet's DOTNET_ROOT) to this shell, and
    # persist it to /etc/profile.d for any interactive login shell. Neither Claude Code nor a
    # GitHub Actions step sources /etc/profile.d, so this alone doesn't reach later invocations —
    # see link_dotnet_wrapper below.
    while IFS=$'\t' read -r env_key env_value; do
        [ -z "${env_key}" ] && continue
        eval "export ${env_key}=\"${env_value}\""
        echo "export ${env_key}=\"${env_value}\"" >> /etc/profile.d/agentbox-features.sh
    done < <(jq -r '.containerEnv // {} | to_entries[] | "\(.key)\t\(.value)"' "${feat_dir}/devcontainer-feature.json" 2>/dev/null)

    if [ -n "${DOTNET_ROOT:-}" ] && [ -x "${DOTNET_ROOT}/dotnet" ]; then
        link_dotnet_wrapper dotnet "${DOTNET_ROOT}/dotnet"
    fi
}

# Writes /usr/local/bin/<name> as a wrapper that exports DOTNET_ROOT before exec'ing real_path.
link_dotnet_wrapper() {
    local name="$1" real_path="$2"
    cat > "/usr/local/bin/${name}" <<WRAPPER
#!/bin/bash
export DOTNET_ROOT="${DOTNET_ROOT:-}"
exec "${real_path}" "\$@"
WRAPPER
    chmod +x "/usr/local/bin/${name}"
}

n=0
while IFS= read -r entry; do
    n=$((n + 1))
    install_feature "${entry}" "${n}"
done < <(jq -c '.installOrder[]' <<<"${RESOLVED}")

# dotnet global tools (pac, txc) land in ~/.dotnet/tools and also need DOTNET_ROOT at run time.
[ -x "${HOME}/.dotnet/tools/pac" ] && link_dotnet_wrapper pac "${HOME}/.dotnet/tools/pac"
[ -x "${HOME}/.dotnet/tools/txc" ] && link_dotnet_wrapper txc "${HOME}/.dotnet/tools/txc"

dotnet new install TALXIS.DevKit.Templates.Dataverse || true

# Register the TALXIS/skills plugin (Skills + the txc MCP server) with whichever harness this
# surface actually uses. AGENTBOX_HARNESS narrows this to just the relevant harness on the two
# cloud/CI surfaces that set it; unset (manual/local runs) tries both, skipping whichever binary
# isn't installed.
merge_json_file() {
    local file="$1" filter="$2"
    mkdir -p "$(dirname "${file}")"
    local current="{}"
    [ -s "${file}" ] && current="$(cat "${file}")"
    jq "${filter}" <<<"${current}" > "${file}.tmp" && mv "${file}.tmp" "${file}"
}

if [ "${AGENTBOX_HARNESS:-}" != "copilot" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ] && command -v claude >/dev/null 2>&1; then
    echo "--- Registering the talxis plugin marketplace with Claude Code ---"
    export CLAUDE_CODE_PLUGIN_CACHE_DIR="/usr/local/claude-plugin-seed"
    timeout 30 claude plugin marketplace add TALXIS/skills \
        || echo "WARNING: could not add the talxis plugin marketplace, continuing" >&2
    timeout 30 claude plugin install implementation@talxis --yes \
        || echo "WARNING: could not install the implementation@talxis plugin, continuing" >&2
fi

if [ "${AGENTBOX_HARNESS:-}" != "claude" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ]; then
    echo "--- Registering the talxis plugin marketplace with GitHub Copilot ---"
    curl -fsSL --max-time 20 "https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/scripts/install-talxis-plugin.sh" \
        -o "${WORKDIR}/install-talxis-plugin.sh" \
        && bash "${WORKDIR}/install-talxis-plugin.sh"

    # Declarative form too, so `copilot plugin update` finds it without re-adding the marketplace,
    # and so a fresh user created later from /etc/skel starts with it already declared.
    marketplace_filter='.extraKnownMarketplaces.talxis = {"source": {"source": "github", "repo": "TALXIS/skills"}} |
        .enabledPlugins["implementation@talxis"] = true'
    merge_json_file "${HOME}/.copilot/settings.json" "${marketplace_filter}"
    merge_json_file "/etc/skel/.copilot/settings.json" "${marketplace_filter}"
fi

# install_feature() logs failures but does not stop on them; report final status per tool.
echo "=== Tool check ==="
missing=0
for tool in dotnet az pwsh terraform gh copilot func pac txc; do
    if command -v "${tool}" >/dev/null 2>&1; then
        echo "OK   ${tool} -> $(command -v "${tool}")"
    else
        echo "MISSING ${tool}"
        missing=1
    fi
done
[ "${missing}" -eq 1 ] && echo "One or more tools are missing — see the install output above for the matching WARNING line."

exit 0
