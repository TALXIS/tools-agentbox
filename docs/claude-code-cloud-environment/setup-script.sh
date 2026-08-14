#!/bin/bash
# Claude Code cloud environment setup script for the Power Platform / agentbox toolchain.
#
# Paste this into the environment's "Setup script" field at
# claude.ai/admin-settings/cloud-environments. It runs once (root, before Claude Code
# launches) and is cached for ~7 days, so its only job is to warm the cache: install the
# devcontainer CLI and pre-pull the agentbox image so every session's `devcontainer up`
# (run by the SessionStart hook shipped in src/templates/power-platform/.claude/settings.json)
# starts from an image that's already on disk instead of pulling it fresh.
#
# Do not start the container here — the environment cache keeps files on disk, not running
# processes, so anything started in this script won't be running by the time a later,
# cached session begins.

npm install -g @devcontainers/cli || true
docker pull ghcr.io/talxis/tools-agentbox/image:latest || true
