#!/usr/bin/env bash
# Packs the agentbox tool: pins the AgentBox Feature list (versions to digests, install order) into
# the embedded lockfile, then packs. Usage: pack.sh <version> <output dir>
# Needs jq and npm (for the devcontainer CLI, used only here, never at run time).
set -euo pipefail

version="$1"
out="$2"
cli="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="${cli}/../container/templates/power-platform/.devcontainer/devcontainer.features.json"

command -v devcontainer >/dev/null 2>&1 || npm install -g @devcontainers/cli --silent

workdir="$(mktemp -d)"
trap 'rm -rf "${workdir}"' EXIT
mkdir -p "${workdir}/.devcontainer"
jq '{features: (.features + {"ghcr.io/devcontainers/features/dotnet:1": {"version": "10.0"}})}' "${manifest}" \
    > "${workdir}/.devcontainer/devcontainer.json"

# resolve-dependencies prints a diagram before its JSON; keep the JSON.
resolved="$(devcontainer features resolve-dependencies --workspace-folder "${workdir}" | sed -n '/^{/,$p')"
jq -e '.installOrder | length > 0' <<<"${resolved}" >/dev/null || { echo "Could not resolve ${manifest}" >&2; exit 1; }
jq '{features: [.installOrder[] | {id, options}]}' <<<"${resolved}" > "${cli}/TALXIS.AgentBox.CLI/features.lock.json"

dotnet pack "${cli}/TALXIS.AgentBox.CLI" --configuration Release -p:Version="${version}" --output "${out}"
