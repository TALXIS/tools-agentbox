#!/usr/bin/env bash
# Prints the image's devcontainer.metadata label: one entry holding the start/create hooks of this
# repo's Features in the image, plus devcontainer-metadata.json. The image isn't built by the
# devcontainer CLI, which would otherwise write this label. One entry, object-form commands: they
# run in parallel.
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

jq -c --argjson create "$(hooks postCreateCommand)" --argjson start "$(hooks postStartCommand)" '
    [ . + {
        postCreateCommand: ($create + (.postCreateCommand // {})),
        postStartCommand: ($start + (.postStartCommand // {}))
    } ]' "${repo}/src/container/image/power-platform/devcontainer-metadata.json"
