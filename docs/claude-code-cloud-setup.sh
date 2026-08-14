#!/bin/bash
# Claude Code cloud environment setup script. Installs the devcontainer Features
# (https://containers.dev) listed in this repo's
# src/templates/power-platform/.devcontainer/devcontainer.features.json — the single source of
# truth for this toolchain, also used by the power-platform template. Nothing here hand-lists
# tools: this script fetches that manifest, asks the real `devcontainer` CLI to resolve versions
# and install order (no Docker needed for that step), then installs each Feature's actual
# published OCI artifact straight onto this VM — there's no Docker daemon in Claude Code cloud
# environments, so Features run directly on the host instead of layered into a container image.
#
# Paste into "Setup script" at claude.ai/admin-settings/cloud-environments. Two other fields on
# that same environment are required, not optional:
# - Environment variables: DOTNET_ROOT=/usr/local/dotnet/current — this script's own wrapper
#   scripts export it for `dotnet`/`pac`/`txc` when called by their wrapped name on the expected
#   PATH entry, but anything that reaches the raw binary a different way (e.g. a PATH order copied
#   from a real devcontainer, where DOTNET_ROOT is already a container-wide ENV) needs the real
#   env var too.
# - Network access: Custom, with the defaults included, plus `cli.github.com` — the github-cli
#   Feature always needs it, it's not on the default Trusted list (ghcr.io and registry.npmjs.org
#   already are, so nothing to add for those). If a Feature's install still fails, allow whatever
#   host it printed and retry — `aka.ms`, `keybase.io`, and `packages.microsoft.com` have come up
#   before.
set -uo pipefail

# However this script gets invoked, $HOME isn't guaranteed to be set — if it's empty, every path
# built from it below (including where `dotnet tool install --global` actually lands) silently
# resolves relative to `/` instead of this user's real home. Pin it up front instead of trusting
# the inherited environment.
export HOME="${HOME:-$(eval echo "~$(id -un)")}"

FEATURES_MANIFEST_URL="https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/templates/power-platform/.devcontainer/devcontainer.features.json"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

export _CONTAINER_USER="$(id -un)"
export _REMOTE_USER="${_CONTAINER_USER}"
export _REMOTE_USER_HOME="${HOME}"

npm install -g @devcontainers/cli --silent

# Bare VM has no base image, so unlike the container-based consumers of devcontainer.features.json,
# dotnet isn't already present and has to be bootstrapped before that manifest's own features
# (several of which — pac-cli, txc-cli — need dotnet) can install.
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

    # A real `devcontainer build` bakes each Feature's declared containerEnv (e.g. dotnet's
    # DOTNET_ROOT) into the image as ENV directives, so every process started in that container
    # inherits it unconditionally. Installing straight onto the VM has no equivalent: Claude's own
    # command execution doesn't go through a login or interactive shell, so neither
    # /etc/profile.d nor ~/.bashrc ever gets sourced, and containerEnv vars set here would
    # otherwise vanish the moment this script exits. /etc/profile.d is still written below for
    # anyone who does open an interactive login shell, but nothing load-bearing can depend on it.
    while IFS=$'\t' read -r env_key env_value; do
        [ -z "${env_key}" ] && continue
        eval "export ${env_key}=\"${env_value}\""
        echo "export ${env_key}=\"${env_value}\"" >> /etc/profile.d/agentbox-features.sh
    done < <(jq -r '.containerEnv // {} | to_entries[] | "\(.key)\t\(.value)"' "${feat_dir}/devcontainer-feature.json" 2>/dev/null)

    # So: wrap `dotnet` itself in a script that sets DOTNET_ROOT before exec'ing the real binary,
    # baking the env var into every future invocation regardless of which shell (if any) started
    # it. link_dotnet_wrapper (defined below, used again after the loop for pac/txc) does the same
    # for anything that's actually a "dotnet tool install --global" shim.
    if [ -n "${DOTNET_ROOT:-}" ] && [ -x "${DOTNET_ROOT}/dotnet" ]; then
        link_dotnet_wrapper dotnet "${DOTNET_ROOT}/dotnet"
    fi
}

# Writes /usr/local/bin/<name> as a wrapper that exports DOTNET_ROOT (captured now, from
# whichever Feature set it) before exec'ing the real binary — see the comment above.
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

# dotnet global tools (pac, txc) land in ~/.dotnet/tools, not on PATH for the rest of this script,
# and — like `dotnet` itself above — need DOTNET_ROOT set at run time, not just install time.
[ -x "${HOME}/.dotnet/tools/pac" ] && link_dotnet_wrapper pac "${HOME}/.dotnet/tools/pac"
[ -x "${HOME}/.dotnet/tools/txc" ] && link_dotnet_wrapper txc "${HOME}/.dotnet/tools/txc"

dotnet new install TALXIS.DevKit.Templates.Dataverse || true

# install_feature() swallows failures so one broken Feature doesn't stop the rest — which also
# means a green "Ran setup script" tells you nothing about whether any given tool actually made
# it. Report on every tool explicitly so a missing one is visible in this script's own output.
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
