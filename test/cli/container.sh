#!/usr/bin/env bash
# A bare ubuntu:24.04 container, as root: agentbox installs everything, and an update right after has
# nothing to do.
source "$(dirname "$0")/common.sh"

step "bootstrap"
apt-get update -qq && apt-get install -y -qq curl ca-certificates jq libicu74 >/dev/null  # .NET needs libicu
bootstrap_dotnet /opt/dotnet-bootstrap
install_agentbox

step "prerequisite install"
agentbox prerequisite install --format text
hash -r
check_ready

step "prerequisite update right after the install changes nothing"
agentbox prerequisite update --format json > /tmp/update.json
jq -e '[.[] | select(.action == "updated" or .action == "failed")] | length == 0' /tmp/update.json >/dev/null \
    || { cat /tmp/update.json; exit 1; }
