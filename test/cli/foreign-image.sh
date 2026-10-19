#!/usr/bin/env bash
# Another vendor's devcontainer image, as its non-root user with passwordless sudo: agentbox installs
# the system prerequisites through sudo and the user-scope ones for the user.
source "$(dirname "$0")/common.sh"

step "bootstrap (as $(id -un))"
command -v jq >/dev/null 2>&1 || { sudo apt-get update -qq && sudo apt-get install -y -qq jq >/dev/null; }
bootstrap_dotnet "${HOME}/.dotnet-bootstrap"
install_agentbox

step "prerequisite install"
agentbox prerequisite install --strict --format text
hash -r

step "prerequisite validate (as $(id -un))"
agentbox prerequisite validate --format text
