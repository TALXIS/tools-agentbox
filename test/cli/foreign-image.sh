#!/usr/bin/env bash
# The setup script in another vendor's devcontainer image, as its non-root user, as the GitHub
# Copilot cloud agent runs it: system prerequisites go in through sudo, per-user ones for the user.
source "$(dirname "$0")/common.sh"

step "setup script (as $(id -un))"
setup
hash -r
check_ready
