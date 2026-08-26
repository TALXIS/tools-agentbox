#!/bin/bash
# Configures whichever agent harness is present — Claude Code and/or GitHub Copilot CLI — beyond
# just installing their binaries. Currently: registers the TALXIS/skills plugin (Skills + the
# `txc` MCP server). Named generically, not after that one thing, since this is the intended home
# for any other per-harness setup agentbox ends up needing (e.g. other marketplaces, settings.json
# tweaks) — one script per harness-configuration concern, not one script per plugin.
#
# No-ops for a harness whose binary isn't installed, or one explicitly excluded via
# AGENTBOX_HARNESS ("claude", "copilot", or "none" to skip both — see src/container/scripts/install-features.sh
# for who sets this and why).
#
# Called from src/container/scripts/install-features.sh (Claude Code cloud, Copilot cloud sandbox), and
# fetched/run directly from a repo's own devcontainer.json postCreateCommand (Codespaces — only
# Copilot is ever relevant there, but this script is reused rather than maintaining a Copilot-only
# copy). Per VS Code's own docs, VS Code's Copilot Chat automatically discovers plugins installed
# this way, so one install covers both the `copilot` CLI and VS Code.
set -uo pipefail

merge_json_file() {
    local file="$1" filter="$2"
    mkdir -p "$(dirname "${file}")"
    local current="{}"
    [ -s "${file}" ] && current="$(cat "${file}")"
    jq "${filter}" <<<"${current}" > "${file}.tmp" && mv "${file}.tmp" "${file}"
}

if [ "${AGENTBOX_HARNESS:-}" != "copilot" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ] \
        && command -v claude >/dev/null 2>&1; then
    echo "--- Registering the talxis plugin marketplace with Claude Code ---"
    export CLAUDE_CODE_PLUGIN_CACHE_DIR="/usr/local/claude-plugin-seed"
    timeout 30 claude plugin marketplace add TALXIS/skills \
        || echo "WARNING: could not add the talxis plugin marketplace, continuing" >&2
    timeout 30 claude plugin install implement@talxis --yes \
        || echo "WARNING: could not install the implement@talxis plugin, continuing" >&2
fi

if [ "${AGENTBOX_HARNESS:-}" != "claude" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ] \
        && command -v copilot >/dev/null 2>&1; then
    echo "--- Registering the talxis plugin marketplace with GitHub Copilot ---"
    timeout 30 copilot plugin marketplace add TALXIS/skills >/dev/null 2>&1 \
        || echo "WARNING: could not add the talxis plugin marketplace, continuing" >&2
    timeout 30 copilot plugin install implement@talxis >/dev/null 2>&1 \
        || echo "WARNING: could not install the implement@talxis plugin, continuing" >&2

    # Declarative form too, so `copilot plugin update` finds it without re-adding the marketplace,
    # and so a fresh user created later from /etc/skel starts with it already declared.
    marketplace_filter='.extraKnownMarketplaces.talxis = {"source": {"source": "github", "repo": "TALXIS/skills"}} |
        .enabledPlugins["implement@talxis"] = true'
    merge_json_file "${HOME:-/root}/.copilot/settings.json" "${marketplace_filter}"
    merge_json_file "/etc/skel/.copilot/settings.json" "${marketplace_filter}"
fi

exit 0
