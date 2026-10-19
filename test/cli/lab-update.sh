#!/usr/bin/env bash
# The lab scenario, in the published image: tools that follow the latest release are behind (an old
# Codespace), and one agentbox command brings them up to date without a restart.
source "$(dirname "$0")/common.sh"

previous_nuget() {
    curl -fsSL "https://api.nuget.org/v3-flatcontainer/$(tr '[:upper:]' '[:lower:]' <<<"$1")/index.json" \
        | jq -r '[.versions[] | select(test("-") | not)][-2]'
}

step "make the image's tools outdated"
# Where the image has it: the shared tool path, or (images before it) the user's global tools.
downgrade_tool() {
    local package="$1" tool="$2" where=(--global)
    [ -d "/usr/local/share/${tool}" ] && where=(--tool-path "/usr/local/share/${tool}")
    dotnet tool update "${package}" "${where[@]}" --version "$(previous_nuget "${package}")" --allow-downgrade
}
downgrade_tool TALXIS.CLI txc
downgrade_tool Microsoft.PowerApps.CLI.Tool pac
dotnet new install "TALXIS.DevKit.Templates.Dataverse::$(previous_nuget TALXIS.DevKit.Templates.Dataverse)"
pa_previous="$(npm view @microsoft/power-apps-cli versions --json | jq -r '[.[] | select(test("-") | not)][-2]')"
npm install --global --prefix /usr/local/lib/power-apps-cli "@microsoft/power-apps-cli@${pa_previous}" >/dev/null

agentbox() { dotnet dnx --add-source "${AGENTBOX_NUPKG:-/nupkg}" TALXIS.AgentBox.CLI --version "${AGENTBOX_VERSION}" --yes "$@"; }

step "validate --check-updates reports them"
agentbox prerequisite validate --check-updates --format json | jq -e '[.[] | select(.status == "outdated")] | length >= 4' >/dev/null

step "the lab instruction: prerequisite update"
agentbox prerequisite update --format text

step "everything is there and current"
agentbox prerequisite validate --check-updates --format json > /tmp/after.json
jq -e 'all(.[]; .status == "ok" or .status == "not-needed")' /tmp/after.json >/dev/null \
    || { cat /tmp/after.json; exit 1; }
