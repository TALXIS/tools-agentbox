#!/usr/bin/env bash
# Another vendor's devcontainer image, as its non-root user: system prerequisites go in through sudo,
# per-user ones for the user.
source "$(dirname "$0")/common.sh"

step "bootstrap (as $(id -un))"
bootstrap_dotnet "${HOME}/.dotnet-bootstrap"
install_agentbox

step "prerequisite install"
agentbox prerequisite install --format text
hash -r
check_ready
