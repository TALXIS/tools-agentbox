#!/usr/bin/env bash
# The lab scenario, in the published image: tools that follow the latest release are behind (an old
# Codespace), and one agentbox command brings them up to date without a restart.
source "$(dirname "$0")/common.sh"

previous_nuget() {
    curl -fsSL "https://api.nuget.org/v3-flatcontainer/$(tr '[:upper:]' '[:lower:]' <<<"$1")/index.json" \
        | jq -r '[.versions[] | select(test("-") | not)][-2]'
}

step "make the image's tools outdated"
dotnet tool update --global TALXIS.CLI --version "$(previous_nuget TALXIS.CLI)" --allow-downgrade
dotnet tool update --global Microsoft.PowerApps.CLI.Tool --version "$(previous_nuget Microsoft.PowerApps.CLI.Tool)" --allow-downgrade
dotnet new install "TALXIS.DevKit.Templates.Dataverse::$(previous_nuget TALXIS.DevKit.Templates.Dataverse)"
pa_previous="$(npm view @microsoft/power-apps-cli versions --json | jq -r '[.[] | select(test("-") | not)][-2]')"
npm install --global --prefix /usr/local/lib/power-apps-cli "@microsoft/power-apps-cli@${pa_previous}" >/dev/null

agentbox() { dotnet dnx --add-source /nupkg TALXIS.AgentBox.CLI --yes -- "$@"; }

step "install agentbox itself, which images before it came with lack"
agentbox prerequisite install agentbox --format text

step "validate --check-updates reports them"
agentbox prerequisite validate --check-updates --format json | jq -e '[.[] | select(.status == "outdated")] | length >= 4' >/dev/null

step "the lab instruction: prerequisite update"
agentbox prerequisite update --format text

step "everything is there and current"
agentbox prerequisite validate --check-updates --format json > /tmp/after.json
jq -e 'all(.[]; .status == "ok" or .status == "not-needed")' /tmp/after.json >/dev/null \
    || { cat /tmp/after.json; exit 1; }
