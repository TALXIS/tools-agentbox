#!/bin/bash
# Claude Code cloud environment setup script. Runs the same devcontainer Features
# (https://containers.dev) as this repo's power-platform template — ours and upstream — directly
# on the VM instead of inside a container, so nothing is hand-duplicated from a Dockerfile.
# Paste into "Setup script" at claude.ai/admin-settings/cloud-environments. Network access:
# Custom, allow cli.github.com and aka.ms in addition to the defaults (a step below prints which
# host it needed if it fails — check the session's DNS audit trail and add it here).
set -uo pipefail

feature() { curl -fsSL "https://raw.githubusercontent.com/$1/install.sh" | bash; }

VERSION=10.0 feature devcontainers/features/main/src/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet 2>/dev/null || true

VERSION=latest EXTENSIONS=azure-devops feature devcontainers/features/main/src/azure-cli &
VERSION=latest                         feature devcontainers/features/main/src/powershell &
VERSION=latest                         feature devcontainers/features/main/src/terraform &
VERSION=latest                         feature devcontainers/features/main/src/github-cli &
VERSION=latest                         feature devcontainers/features/main/src/copilot-cli &
VERSION=latest                         feature jlaundry/devcontainer-features/main/src/azure-functions-core-tools &
wait

VERSION=latest feature TALXIS/tools-agentbox/master/src/features/pac-cli
ln -sf "$HOME/.dotnet/tools/pac" /usr/local/bin/pac 2>/dev/null || true

VERSION=latest feature TALXIS/tools-agentbox/master/src/features/txc-cli
ln -sf "$HOME/.dotnet/tools/txc" /usr/local/bin/txc 2>/dev/null || true

dotnet new install TALXIS.DevKit.Templates.Dataverse || true

exit 0
