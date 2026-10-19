#!/usr/bin/env bash
# Prints the image's devcontainer.metadata label: one entry holding the create and start hooks of
# this repo's Features in the image. The image isn't built by the devcontainer CLI, which would
# otherwise write this label. One entry, object-form commands: they run in parallel.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
manifest="${repo}/src/container/templates/power-platform/.devcontainer/devcontainer.features.json"

hooks() {
    local hook="$1"
    for id in $(jq -r '.features | keys[]' "${manifest}" | sed -n 's#^ghcr\.io/talxis/tools-agentbox/\([^:]*\):.*#\1#p'); do
        jq -c --arg hook "${hook}" --arg id "${id}" 'select(.[$hook]) | {($id): .[$hook]}' \
            "${repo}/src/container/features/${id}/devcontainer-feature.json"
    done | jq -cs 'add // {}'
}

jq -nc --argjson create "$(hooks postCreateCommand)" --argjson start "$(hooks postStartCommand)" \
    '[{id: "ghcr.io/talxis/tools-agentbox/image", postCreateCommand: $create, postStartCommand: $start}]'
