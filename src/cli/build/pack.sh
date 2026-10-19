#!/usr/bin/env bash
# Packs the agentbox tool: pins the AgentBox Feature list (versions to digests, install order, and
# what each Feature depends on) into the embedded lockfile, then packs. Usage: pack.sh <version> <output dir>
# With AGENTBOX_FEATURES_FROM (CI), this repo's Features come from that directory, unpinned, so a
# pull request can add or change one before it's published.
# Needs jq and npm (for the devcontainer CLI, used only here, never at run time).
set -euo pipefail

version="$1"
out="$2"
cli="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="${cli}/../container/agentbox-features.json"

command -v devcontainer >/dev/null 2>&1 || npm install -g @devcontainers/cli@0.89.0 --silent

workdir="$(mktemp -d)"
trap 'rm -rf "${workdir}"' EXIT
mkdir -p "${workdir}/.devcontainer"
this_repo="ghcr.io/talxis/tools-agentbox/"
if [ -n "${AGENTBOX_FEATURES_FROM:-}" ]; then
    cp -r "${AGENTBOX_FEATURES_FROM}/." "${workdir}/.devcontainer/"
    jq --arg repo "${this_repo}" '.features |= with_entries(.key |= if startswith($repo)
        then "./" + (ltrimstr($repo) | sub(":.*$"; "")) else . end)' "${manifest}" > "${workdir}/.devcontainer/devcontainer.json"
else
    cp "${manifest}" "${workdir}/.devcontainer/devcontainer.json"
fi

# resolve-dependencies prints a diagram before its JSON; keep the JSON.
resolved="$(devcontainer features resolve-dependencies --workspace-folder "${workdir}" | sed -n '/^{/,$p')"
jq -e '.installOrder | length > 0' <<<"${resolved}" >/dev/null || { echo "Could not resolve ${manifest}" >&2; exit 1; }

# Each Feature's dependsOn, as registry paths, so installing one prerequisite brings what it needs.
features='[]'
while IFS= read -r entry; do
    id="$(jq -r .id <<<"${entry}")"
    if [[ "${id}" == ./* ]]; then
        metadata="$(cat "${workdir}/.devcontainer/${id#./}/devcontainer-feature.json")"
        entry="$(jq -c --arg id "${this_repo}${id#./}" '.id = $id' <<<"${entry}")"
    else
        metadata="$(devcontainer features info manifest "${id}" --output-format json \
            | jq '.manifest.annotations["dev.containers.metadata"] | fromjson')"
    fi
    depends="$(jq -c '[.dependsOn // {} | keys[] | sub("^[^/]+/"; "") | sub("[:@].*$"; "")]' <<<"${metadata}")"
    features="$(jq -c --argjson entry "${entry}" --argjson depends "${depends}" '. + [$entry + {dependsOn: $depends}]' <<<"${features}")"
done < <(jq -c '.installOrder[] | {id, options}' <<<"${resolved}")
# A local Feature and its published copy (another one's dependency) are the same Feature: keep the first.
jq '{features: (reduce .[] as $f ([]; if any(.[]; (.id | sub("@.*$"; "")) == ($f.id | sub("@.*$"; ""))) then . else . + [$f] end))}' \
    <<<"${features}" > "${workdir}/features.lock.json"

dotnet pack "${cli}/TALXIS.DevKit.AgentBox.CLI" --configuration Release -p:Version="${version}" \
    -p:AgentBoxFeatureLock="${workdir}/features.lock.json" --output "${out}"
