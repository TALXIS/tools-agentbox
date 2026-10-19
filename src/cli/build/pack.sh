#!/usr/bin/env bash
# Packs the agentbox tool: pins the AgentBox Feature list (versions to digests, install order, and
# what each Feature depends on) into the embedded lockfile, then packs. Usage: pack.sh <version> <output dir>
# Needs jq and npm (for the devcontainer CLI, used only here, never at run time).
set -euo pipefail

version="$1"
out="$2"
cli="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="${cli}/../container/templates/power-platform/.devcontainer/devcontainer.features.json"

command -v devcontainer >/dev/null 2>&1 || npm install -g @devcontainers/cli@0.89.0 --silent

workdir="$(mktemp -d)"
trap 'rm -rf "${workdir}"' EXIT
mkdir -p "${workdir}/.devcontainer"
cp "${manifest}" "${workdir}/.devcontainer/devcontainer.json"

# resolve-dependencies prints a diagram before its JSON; keep the JSON.
resolved="$(devcontainer features resolve-dependencies --workspace-folder "${workdir}" | sed -n '/^{/,$p')"
jq -e '.installOrder | length > 0' <<<"${resolved}" >/dev/null || { echo "Could not resolve ${manifest}" >&2; exit 1; }

# Each Feature's dependsOn, as registry paths, so installing one prerequisite brings what it needs.
features='[]'
while IFS= read -r entry; do
    depends="$(devcontainer features info manifest "$(jq -r .id <<<"${entry}")" --output-format json \
        | jq -c '[.manifest.annotations["dev.containers.metadata"] | fromjson | .dependsOn // {} | keys[] | sub("^[^/]+/"; "") | sub("[:@].*$"; "")]')"
    features="$(jq -c --argjson entry "${entry}" --argjson depends "${depends}" '. + [$entry + {dependsOn: $depends}]' <<<"${features}")"
done < <(jq -c '.installOrder[] | {id, options}' <<<"${resolved}")
jq '{features: .}' <<<"${features}" > "${workdir}/features.lock.json"

dotnet pack "${cli}/TALXIS.AgentBox.CLI" --configuration Release -p:Version="${version}" \
    -p:AgentBoxFeatureLock="${workdir}/features.lock.json" --output "${out}"
