#!/bin/bash
# Registers the TALXIS/skills plugin marketplace with GitHub Copilot CLI and installs the
# `implementation` plugin (Skills + the `txc` MCP server). No-ops if `copilot` isn't installed.
#
# Called from src/scripts/install-features.sh (Copilot cloud sandbox), and fetched/run directly
# from the Power Platform template's devcontainer.json postCreateCommand (Codespaces) — per VS
# Code's own docs, VS Code's Copilot Chat automatically discovers plugins installed this way, so
# one install here covers both the `copilot` CLI and VS Code Copilot Chat.
set -uo pipefail

command -v copilot >/dev/null 2>&1 || exit 0

timeout 30 copilot plugin marketplace add TALXIS/skills >/dev/null 2>&1 \
    || echo "WARNING: could not add the talxis plugin marketplace, continuing" >&2
timeout 30 copilot plugin install implementation@talxis >/dev/null 2>&1 \
    || echo "WARNING: could not install the implementation@talxis plugin, continuing" >&2

exit 0
