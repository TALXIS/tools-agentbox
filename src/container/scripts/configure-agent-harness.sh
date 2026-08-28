#!/bin/bash
# Configures whichever agent harness is present — Claude Code and/or GitHub Copilot CLI — from the
# single source of truth in src/agent/, so all three knobs are configured in one place for every
# surface (Claude Code cloud, Copilot cloud sandbox, Codespaces, Docker) and every harness:
#
#   agent.json         which plugin marketplaces/plugins to register (the Skills list)
#   system-prompt.md   instructions loaded into every session, on every harness
#   initial-message.md context injected once per session, at session start
#
# What each knob maps to per harness (see src/agent/README.md for the full matrix and why):
#
#   knob            Claude Code                                  GitHub Copilot CLI
#   plugins         claude plugin marketplace add / install      copilot plugin ... + ~/.copilot/settings.json
#   system prompt   /etc/claude-code/CLAUDE.md (managed policy)  ~/.copilot/copilot-instructions.md
#                   ~/.claude/CLAUDE.md when not root
#   initial message SessionStart hook in ~/.claude/settings.json ~/.copilot/hooks/agentbox.json (sessionStart)
#
# Both harnesses treat these as context, not enforcement: they steer the model, they do not constrain
# it. Anything that must hold regardless of what the model decides belongs in a permission rule or a
# PreToolUse/preToolUse hook instead.
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
#
# Re-running is safe and is the intended way to pick up an edit to src/agent/: every write below is
# idempotent (JSON merges, and a marked block in the markdown files that is replaced, not appended).
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKDIR="$(mktemp -d)"
trap 'rm -rf "${WORKDIR}"' EXIT

# Where the agent config comes from, in order: an explicit AGENTBOX_CONFIG_DIR, a src/agent/ sibling
# when this script runs from a checkout, then the network. The short link is preferred over the raw
# URL for the same reason install-features.sh uses one (no branch/path hardcoded in a published
# command), but the raw URL is tried too so this works before the redirect exists.
AGENT_CONFIG_URL="${AGENTBOX_CONFIG_URL:-https://talxis.com/agentbox-agent}"
AGENT_CONFIG_URL_RAW="https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/agent/agent.json"

# Used when src/agent/agent.json can't be reached at all, so an offline run still registers the
# plugin exactly as this script did before it was config-driven, instead of silently doing nothing.
DEFAULT_MANIFEST='{"marketplaces":{"talxis":"TALXIS/skills"},"plugins":["implement@talxis"]}'

BLOCK_BEGIN="<!-- BEGIN AGENTBOX: managed by TALXIS/tools-agentbox (src/agent/system-prompt.md) -->"
BLOCK_END="<!-- END AGENTBOX -->"

is_root() { [ "$(id -u)" -eq 0 ]; }

merge_json_file() {
    local file="$1" filter="$2"
    shift 2
    mkdir -p "$(dirname "${file}")"
    local current="{}"
    [ -s "${file}" ] && current="$(cat "${file}")"
    jq "$@" "${filter}" <<<"${current}" > "${file}.tmp" && mv "${file}.tmp" "${file}"
}

# Replaces (or, when the payload is empty/missing, removes) the AGENTBOX block in a markdown file,
# leaving anything a developer wrote around it untouched — these files are shared with the user
# (~/.copilot/copilot-instructions.md especially), so this must never be a blind overwrite.
apply_marked_block() {
    local file="$1" payload="${2:-}" tmp block
    tmp="${WORKDIR}/block-target.$$"
    block="${WORKDIR}/block-body.$$"
    : > "${block}"
    if [ -n "${payload}" ] && [ -s "${payload}" ]; then
        { printf '%s\n' "${BLOCK_BEGIN}"; cat "${payload}"; printf '%s\n' "${BLOCK_END}"; } > "${block}"
    fi

    mkdir -p "$(dirname "${file}")" 2>/dev/null || return 0

    if [ -f "${file}" ] && grep -qF "${BLOCK_BEGIN}" "${file}"; then
        awk -v b="${BLOCK_BEGIN}" -v e="${BLOCK_END}" -v bf="${block}" '
            index($0, b) { while ((getline line < bf) > 0) print line; close(bf); skip = 1; next }
            index($0, e) { skip = 0; next }
            !skip
        ' "${file}" > "${tmp}" || return 0
    elif [ -s "${block}" ]; then
        { [ -s "${file}" ] && { cat "${file}"; printf '\n'; }; cat "${block}"; } > "${tmp}" || return 0
    else
        return 0
    fi

    mv "${tmp}" "${file}" || return 0
    # A file left with nothing but whitespace (block removed, nothing else in it) is noise.
    grep -q '[^[:space:]]' "${file}" 2>/dev/null || rm -f "${file}"
}

# --- Resolve the config ------------------------------------------------------------------------

download_config() {
    local url="$1" dir="$2" effective base file
    mkdir -p "${dir}"
    effective="$(curl -fsSL --max-time 20 -w '%{url_effective}' -o "${dir}/agent.json" "${url}")" || return 1
    jq -e 'type == "object"' "${dir}/agent.json" >/dev/null 2>&1 || return 1
    # Payload file names in agent.json are relative to the manifest, so resolve them against the URL
    # curl actually ended up on (the short link redirects to raw.githubusercontent.com).
    base="${effective%/*}"
    while IFS= read -r file; do
        [ -z "${file}" ] && continue
        mkdir -p "$(dirname "${dir}/${file}")"
        curl -fsSL --max-time 20 -o "${dir}/${file}" "${base}/${file}" \
            || echo "WARNING: could not download ${base}/${file}, continuing without it" >&2
    done < <(jq -r '[.systemPrompt, .initialMessage] | map(select(type == "string"))[]' "${dir}/agent.json")
}

CONFIG_DIR=""
if [ -n "${AGENTBOX_CONFIG_DIR:-}" ] && [ -f "${AGENTBOX_CONFIG_DIR}/agent.json" ]; then
    CONFIG_DIR="${AGENTBOX_CONFIG_DIR}"
elif [ -f "${SCRIPT_DIR}/../../agent/agent.json" ]; then
    CONFIG_DIR="$(cd "${SCRIPT_DIR}/../../agent" && pwd)"
else
    for url in "${AGENT_CONFIG_URL}" "${AGENT_CONFIG_URL_RAW}"; do
        if download_config "${url}" "${WORKDIR}/agent"; then
            CONFIG_DIR="${WORKDIR}/agent"
            break
        fi
        echo "WARNING: could not fetch the agent config from ${url}" >&2
    done
fi

if [ -n "${CONFIG_DIR}" ]; then
    echo "--- Agent config: ${CONFIG_DIR} ---"
    MANIFEST="${CONFIG_DIR}/agent.json"
else
    echo "WARNING: no agent config available, falling back to the built-in plugin defaults" >&2
    MANIFEST="${WORKDIR}/agent-default.json"
    printf '%s\n' "${DEFAULT_MANIFEST}" > "${MANIFEST}"
fi

resolve_payload() {
    local key="$1" name
    name="$(jq -r --arg k "${key}" '.[$k] // "" | select(type == "string")' "${MANIFEST}" 2>/dev/null)"
    [ -z "${name}" ] && return 0
    [ -s "${CONFIG_DIR}/${name}" ] && printf '%s' "${CONFIG_DIR}/${name}"
}

SYSTEM_PROMPT_FILE="$(resolve_payload systemPrompt)"
INITIAL_MESSAGE_FILE="$(resolve_payload initialMessage)"

# One shared hook script, installed next to the message it prints, emitting whichever JSON shape the
# calling harness expects. Claude Code reads hookSpecificOutput.additionalContext from a SessionStart
# hook; Copilot CLI reads a bare additionalContext from a sessionStart hook.
SHARE_DIR="/usr/local/share/agentbox"
mkdir -p "${SHARE_DIR}" 2>/dev/null || true
[ -w "${SHARE_DIR}" ] 2>/dev/null || SHARE_DIR="${HOME:-/root}/.agentbox"
SESSION_START_SCRIPT="${SHARE_DIR}/session-start.sh"

install_initial_message() {
    [ -n "${INITIAL_MESSAGE_FILE}" ] || return 1
    mkdir -p "${SHARE_DIR}" || return 1
    cp "${INITIAL_MESSAGE_FILE}" "${SHARE_DIR}/initial-message.md" || return 1
    cat > "${SESSION_START_SCRIPT}" <<'HOOK'
#!/bin/bash
# Installed by configure-agent-harness.sh — prints the AgentBox session briefing as session-start
# context. Edit the message in src/agent/initial-message.md, never this generated copy.
message="$(dirname "$(readlink -f "$0")")/initial-message.md"
[ -s "${message}" ] || exit 0
case "${1:-}" in
    claude) jq -Rs '{hookSpecificOutput: {hookEventName: "SessionStart", additionalContext: .}}' < "${message}" ;;
    *)      jq -Rs '{additionalContext: .}' < "${message}" ;;
esac
HOOK
    chmod +x "${SESSION_START_SCRIPT}" || return 1
}

INITIAL_MESSAGE_READY=1
install_initial_message && INITIAL_MESSAGE_READY=0

# --- Claude Code -------------------------------------------------------------------------------

if [ "${AGENTBOX_HARNESS:-}" != "copilot" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ] \
        && command -v claude >/dev/null 2>&1; then
    echo "--- Configuring Claude Code ---"
    export CLAUDE_CODE_PLUGIN_CACHE_DIR="/usr/local/claude-plugin-seed"

    while IFS= read -r repo; do
        [ -z "${repo}" ] && continue
        timeout 30 claude plugin marketplace add "${repo}" \
            || echo "WARNING: could not add the ${repo} plugin marketplace, continuing" >&2
    done < <(jq -r '.marketplaces // {} | to_entries[] | .value' "${MANIFEST}")

    while IFS= read -r plugin; do
        [ -z "${plugin}" ] && continue
        timeout 30 claude plugin install "${plugin}" --yes \
            || echo "WARNING: could not install the ${plugin} plugin, continuing" >&2
    done < <(jq -r '.plugins // [] | .[]' "${MANIFEST}")

    # System prompt: the managed-policy CLAUDE.md is machine-wide, user-independent, and cannot be
    # excluded by a user's claudeMdExcludes — the right home for it when this runs as root. Falling
    # back to the user memory file keeps a non-root Codespaces run working.
    if is_root; then
        apply_marked_block "/etc/claude-code/CLAUDE.md" "${SYSTEM_PROMPT_FILE}"
    else
        apply_marked_block "${HOME:-/root}/.claude/CLAUDE.md" "${SYSTEM_PROMPT_FILE}"
    fi

    # Initial message: a SessionStart hook, matched on startup|resume so it also lands after a
    # session is resumed. Merged into user settings rather than replacing them, and any earlier
    # agentbox entry is dropped so re-runs don't stack up duplicates.
    if [ "${INITIAL_MESSAGE_READY}" -eq 0 ]; then
        merge_json_file "${HOME:-/root}/.claude/settings.json" '
            .hooks.SessionStart = (
                ((.hooks.SessionStart // [])
                    | map(select(((.hooks // []) | map(.command // "") | any(contains("agentbox"))) | not)))
                + [{matcher: "startup|resume", hooks: [{type: "command", command: $cmd}]}]
            )' --arg cmd "${SESSION_START_SCRIPT} claude"
    fi
fi

# --- GitHub Copilot CLI ------------------------------------------------------------------------

if [ "${AGENTBOX_HARNESS:-}" != "claude" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ] \
        && command -v copilot >/dev/null 2>&1; then
    echo "--- Configuring GitHub Copilot CLI ---"

    while IFS= read -r repo; do
        [ -z "${repo}" ] && continue
        timeout 30 copilot plugin marketplace add "${repo}" >/dev/null 2>&1 \
            || echo "WARNING: could not add the ${repo} plugin marketplace, continuing" >&2
    done < <(jq -r '.marketplaces // {} | to_entries[] | .value' "${MANIFEST}")

    while IFS= read -r plugin; do
        [ -z "${plugin}" ] && continue
        timeout 30 copilot plugin install "${plugin}" >/dev/null 2>&1 \
            || echo "WARNING: could not install the ${plugin} plugin, continuing" >&2
    done < <(jq -r '.plugins // [] | .[]' "${MANIFEST}")

    # Declarative form too, so `copilot plugin update` finds it without re-adding the marketplace,
    # and so a fresh user created later from /etc/skel starts with it already declared.
    marketplace_filter='.extraKnownMarketplaces = ((.extraKnownMarketplaces // {}) + ($manifest.marketplaces // {}
            | with_entries({key: .key, value: {source: {source: "github", repo: .value}}})))
        | .enabledPlugins = ((.enabledPlugins // {}) + (($manifest.plugins // []) | map({key: ., value: true}) | from_entries))'

    copilot_dirs=("${HOME:-/root}/.copilot")
    is_root && copilot_dirs+=("/etc/skel/.copilot")

    for dir in "${copilot_dirs[@]}"; do
        merge_json_file "${dir}/settings.json" "${marketplace_filter}" \
            --argjson manifest "$(cat "${MANIFEST}")"

        # System prompt: user-level custom instructions, loaded in every repository.
        apply_marked_block "${dir}/copilot-instructions.md" "${SYSTEM_PROMPT_FILE}"

        # Initial message: a user-level sessionStart hook. Its own file, so this is a plain write —
        # Copilot merges every hook file it finds in the directory.
        if [ "${INITIAL_MESSAGE_READY}" -eq 0 ]; then
            mkdir -p "${dir}/hooks" \
                && jq -n --arg cmd "${SESSION_START_SCRIPT} copilot" \
                    '{version: 1, hooks: {sessionStart: [{type: "command", bash: $cmd, timeoutSec: 15}]}}' \
                    > "${dir}/hooks/agentbox.json"
        fi
    done
fi

exit 0
