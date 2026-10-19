#!/usr/bin/env bash
# Prints the image's devcontainer.metadata label: one entry holding the create and start hooks of
# the Features in the image, so a container from it behaves like one built from those Features. The
# image isn't built by the devcontainer CLI, which would otherwise write this label. One entry,
# object-form commands: they run in parallel. Needs jq and the devcontainer CLI.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
manifest="${repo}/src/container/agentbox-features.json"

# This repo's Features as they are in this checkout; the others as published.
feature_metadata() {
    local id="$1" name
    case "${id}" in
        ghcr.io/talxis/tools-agentbox/*)
            name="${id#ghcr.io/talxis/tools-agentbox/}"
            cat "${repo}/src/container/features/${name%%:*}/devcontainer-feature.json" ;;
        *)
            devcontainer features info manifest "${id}" --output-format json \
                | jq '.manifest.annotations["dev.containers.metadata"] | fromjson' ;;
    esac
}

metadata="$(jq -r '.features | keys[]' "${manifest}" | while IFS= read -r id; do
    name="${id##*/}"
    feature_metadata "${id}" | jq -c --arg name "${name%%:*}" '{name: $name, postCreateCommand, postStartCommand}'
done)"

hooks() {
    jq -cs --arg hook "$1" 'map(select(.[$hook]) | {(.name): .[$hook]}) | add // {}' <<<"${metadata}"
}

jq -nc --argjson create "$(hooks postCreateCommand)" --argjson start "$(hooks postStartCommand)" \
    '[{id: "ghcr.io/talxis/tools-agentbox/image", postCreateCommand: $create, postStartCommand: $start}]'
