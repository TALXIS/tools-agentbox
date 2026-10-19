#!/usr/bin/env bash
# A bare ubuntu:24.04 container, as root: agentbox installs every prerequisite, validates them, and
# a second update finds nothing to do.
source "$(dirname "$0")/common.sh"

step "bootstrap"
apt-get update -qq && # libicu: .NET needs it to start.
apt-get install -y -qq curl ca-certificates jq libicu74 >/dev/null
bootstrap_dotnet /opt/dotnet-bootstrap
install_agentbox

step "prerequisite install"
agentbox prerequisite install --strict --format text
hash -r

step "prerequisite validate"
agentbox prerequisite validate --format text

step "prerequisite update (twice; the second must change nothing)"
agentbox prerequisite update --format text
agentbox prerequisite update --format json > /tmp/second.json
jq -e '[.results[] | select(.action == "updated" or .action == "failed")] | length == 0' /tmp/second.json >/dev/null \
    || { cat /tmp/second.json; echo "second update changed something" >&2; exit 1; }

step "harness list"
agentbox harness list --format text

step "devcontainer create"
mkdir -p /tmp/repo
agentbox devcontainer create /tmp/repo --format text
jq -e '.image == "ghcr.io/talxis/tools-agentbox/image:latest"' /tmp/repo/.devcontainer/devcontainer.json
