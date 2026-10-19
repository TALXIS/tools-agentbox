#!/usr/bin/env bash
# Resolves the AgentBox Feature list (versions to digests, dependency order) once, at pack time,
# into TALXIS.AgentBox.CLI.Core/features.lock.json, which the build embeds. The CLI then installs
# exactly these Features in a Linux container, with no Node.js or devcontainer CLI at run time.
#
# Same inputs as src/container/scripts/install-features.sh: the Feature manifest plus the dotnet
# Feature that script adds. Needs jq and npm (for the devcontainer CLI).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
manifest="${repo_root}/src/container/templates/power-platform/.devcontainer/devcontainer.features.json"
out="${repo_root}/src/cli/TALXIS.AgentBox.CLI.Core/features.lock.json"

command -v devcontainer >/dev/null 2>&1 || npm install -g @devcontainers/cli --silent

workdir="$(mktemp -d)"
trap 'rm -rf "${workdir}"' EXIT
mkdir -p "${workdir}/.devcontainer"
jq '{features: (.features + {"ghcr.io/devcontainers/features/dotnet:1": {"version": "10.0"}})}' \
    "${manifest}" > "${workdir}/.devcontainer/devcontainer.json"

# resolve-dependencies prints a mermaid flowchart before the JSON result; keep only the JSON.
resolved="$(devcontainer features resolve-dependencies --workspace-folder "${workdir}" | sed -n '/^{/,$p')"
jq -e '.installOrder | length > 0' <<<"${resolved}" >/dev/null \
    || { echo "ERROR: could not resolve ${manifest}" >&2; exit 1; }
jq '{features: [.installOrder[] | {id, options}]}' <<<"${resolved}" > "${out}"
echo "Wrote ${out} ($(jq '.features | length' "${out}") Features)"
