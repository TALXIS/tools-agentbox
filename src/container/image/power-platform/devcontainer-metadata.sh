#!/usr/bin/env bash
# Prints the value of the pre-built image's `devcontainer.metadata` label: the devcontainer config
# that every devcontainer tool (Codespaces, VS Code Dev Containers, DevPod, the devcontainer CLI) merges
# into a consumer's devcontainer.json when it uses this image. That's what lets a consumer's file be
# just {"image": "ghcr.io/talxis/tools-agentbox/image:latest"}.
#
# One entry, built from:
#   - devcontainer-metadata.json next to this script: what the image itself needs beyond its
#     Features (the plugin registration on create, the templates update on start). Nothing about
#     the project or the editor: files to open, ports, settings, extensions and machine size are
#     the repository's to set;
#   - the postCreateCommand/postStartCommand of each of this repo's Features in the image's Feature
#     list. The image is built by install-features.sh, not the devcontainer CLI, so their hooks
#     wouldn't reach the label otherwise. The Features stay the only place they're defined.
# Everything goes into one entry's object-form commands so they run in parallel at start;
# separate entries would run one after another.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
manifest="${repo}/src/container/templates/power-platform/.devcontainer/devcontainer.features.json"
extras="${repo}/src/container/image/power-platform/devcontainer-metadata.json"

# Adds a Feature's hook to an object of named commands. String form -> one command named after the
# Feature; object form -> its commands, prefixed with the Feature id. Array (exec) form isn't used by
# any Feature here and is rejected rather than guessed at.
hooks_of() {
    local feature_json="$1" hook="$2" id="$3"
    jq -c --arg hook "${hook}" --arg id "${id}" '
        .[$hook] as $h
        | if $h == null then {}
          elif ($h | type) == "string" then {($id): $h}
          elif ($h | type) == "object" then ($h | with_entries(.key = "\($id)-\(.key)"))
          else error("\($id): array-form \($hook) is not supported")
          end' "${feature_json}"
}

create='{}'
start='{}'
for id in $(jq -r '.features | keys[]' "${manifest}" | sed -n 's#^ghcr\.io/talxis/tools-agentbox/\([^:]*\):.*#\1#p'); do
    feature_json="${repo}/src/container/features/${id}/devcontainer-feature.json"
    create="$(jq -c -n --argjson a "${create}" --argjson b "$(hooks_of "${feature_json}" postCreateCommand "${id}")" '$a + $b')"
    start="$(jq -c -n --argjson a "${start}" --argjson b "$(hooks_of "${feature_json}" postStartCommand "${id}")" '$a + $b')"
done

jq -c --argjson create "${create}" --argjson start "${start}" '
    [ . + {
        postCreateCommand: ($create + (.postCreateCommand // {})),
        postStartCommand: ($start + (.postStartCommand // {}))
    } ]' "${extras}"
