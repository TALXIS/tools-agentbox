#!/bin/bash
# Installs the devcontainer Features (https://containers.dev) listed in
# src/templates/power-platform/.devcontainer/devcontainer.features.json directly onto the host,
# for environments with no Docker daemon (Claude Code cloud) or no devcontainer.json support
# (GitHub Copilot cloud sandbox).
#
# Claude Code cloud environment: paste the bootstrap command from the README's "Claude Code cloud
# environment" section into the environment's "Setup script" field at
# claude.ai/admin-settings/cloud-environments. That environment also requires:
#   - Environment variables: DOTNET_ROOT=/usr/local/dotnet/current
#   - Network access: Custom, defaults included, plus `cli.github.com`
#
# GitHub Copilot cloud sandbox: run via .github/workflows/copilot-setup-steps.yml.
set -uo pipefail

# $HOME may be unset in the invoking environment; every path below depends on it.
export HOME="${HOME:-$(eval echo "~$(id -un)")}"

FEATURES_MANIFEST_URL="https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/templates/power-platform/.devcontainer/devcontainer.features.json"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

export _CONTAINER_USER="$(id -un)"
export _REMOTE_USER="${_CONTAINER_USER}"
export _REMOTE_USER_HOME="${HOME}"

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
