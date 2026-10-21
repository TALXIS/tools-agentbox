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
# A shared tool is deleted and installed again: it can't be updated in place while it's in an image layer.
downgrade_tool() {
    local package="$1" tool="$2" version
    version="$(previous_nuget "${package}")"
    if [ -d "/usr/local/share/${tool}" ]; then
        rm -rf "/usr/local/share/${tool}"
        dotnet tool install "${package}" --tool-path "/usr/local/share/${tool}" --version "${version}"
    else
        dotnet tool update "${package}" --global --version "${version}" --allow-downgrade
    fi
}
downgrade_tool TALXIS.CLI txc
downgrade_tool Microsoft.PowerApps.CLI.Tool pac
dotnet new install "TALXIS.DevKit.Templates.Dataverse::$(previous_nuget TALXIS.DevKit.Templates.Dataverse)"
pa_previous="$(npm view @microsoft/power-apps-cli versions --json | jq -r '[.[] | select(test("-") | not)][-2]')"
npm install --global --prefix /usr/local/lib/power-apps-cli "@microsoft/power-apps-cli@${pa_previous}" >/dev/null

# This build, for dnx and the agentbox Feature alike, as the setup script's --source does.
mkdir -p /etc/opt/NuGet/Config
printf '<configuration><packageSources><add key="ci" value="/nupkg" /></packageSources></configuration>\n' \
    > /etc/opt/NuGet/Config/ci.config
agentbox() { dotnet dnx TALXIS.DevKit.AgentBox.CLI --yes -- "$@"; }

step "install what images before it came without: agentbox itself, Playwright"
agentbox prerequisite install agentbox playwright-cli browser --format text

step "validate --check-updates reports them"
agentbox prerequisite validate --check-updates --format json | jq -e '[.[] | select(.status == "outdated")] | length >= 4' >/dev/null

step "the lab instruction: prerequisite update"
agentbox prerequisite update --format text

step "everything is there and current"
agentbox prerequisite validate --check-updates --format json > /tmp/after.json
jq -e 'all(.[]; .status == "ok" or .status == "not-needed")' /tmp/after.json >/dev/null \
    || { cat /tmp/after.json; exit 1; }
