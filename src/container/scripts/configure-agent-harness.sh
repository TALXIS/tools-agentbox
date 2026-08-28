#!/bin/bash
# Configures whichever agent harness is present — Claude Code and/or GitHub Copilot CLI — from the
# single source of truth in src/agent/, so all three knobs are configured in one place for every
# surface (Claude Code cloud, Copilot cloud sandbox, Codespaces, Docker) and every harness:
#
#   agent.json         the manifest: which plugin marketplaces/plugins to register (the Skills list),
#                      plus the systemPrompt and initialMessage files it points at
#   systemPrompt       instructions loaded into every session, on every harness
#   initialMessage     context injected once per session, at session start
#
# See src/agent/README.md for which path each knob lands in per harness, and why.
#
# Nothing written here is project-scoped: every target is a machine or user path the harness reads
# whatever repository is cloned into the sandbox. Where a harness offers a machine-level path the
# config goes there (Claude's managed-policy CLAUDE.md); otherwise it goes to every home directory
# that could belong to the user who ends up running the harness — the invoking user, /etc/skel for
# users created later, and the user behind sudo, which is the one the Copilot cloud sandbox runs as.
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
# when this script runs from a checkout, then the network. A short link rather than a raw URL for the
# same reason install-features.sh uses one — no branch, path or filename hardcoded anywhere. The
# config exists in exactly one place, so there is no built-in copy to fall back on: a run that cannot
# read it fails instead of half-configuring a box.
AGENT_CONFIG_URL="${AGENTBOX_CONFIG_URL:-https://talxis.com/agentbox-agent}"

BLOCK_BEGIN="<!-- BEGIN AGENTBOX: managed by TALXIS/tools-agentbox -->"
BLOCK_END="<!-- END AGENTBOX -->"

is_root() { [ "$(id -u)" -eq 0 ]; }

# The home of the user behind sudo, when that isn't the invoking user's own. The Copilot cloud
# sandbox provisions with `sudo -E bash install-features.sh`, where HOME resolves to /root, and then
# runs the agent as the unprivileged user — so a config written only to ${HOME} is never read there.
# /etc/skel doesn't cover it either: that user already exists by the time this runs.
sudo_user_home() {
    local home
    is_root || return 1
    [ -n "${SUDO_USER:-}" ] && [ "${SUDO_USER}" != "root" ] || return 1
    home="$(getent passwd "${SUDO_USER}" 2>/dev/null | cut -d: -f6)"
    [ -n "${home}" ] && [ -d "${home}" ] && [ "${home}" != "${HOME:-/root}" ] || return 1
    printf '%s' "${home}"
}

# Hand back anything written into that user's home, so they can edit their own config afterwards.
restore_sudo_user_ownership() {
    [ -n "${SUDO_USER:-}" ] && [ -e "$1" ] || return 0
    chown -R "${SUDO_USER}" "$1" 2>/dev/null || true
}

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

# --- Which harnesses this run configures -------------------------------------------------------

configures_claude() {
    [ "${AGENTBOX_HARNESS:-}" != "copilot" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ] \
        && command -v claude >/dev/null 2>&1
}

configures_copilot() {
    [ "${AGENTBOX_HARNESS:-}" != "claude" ] && [ "${AGENTBOX_HARNESS:-}" != "none" ] \
        && command -v copilot >/dev/null 2>&1
}

# Decided before the config is read, so a run with nothing to configure — the image build passes
# AGENTBOX_HARNESS=none, and a box may simply not have either CLI — never needs the config at all,
# and so can't fail on it.
if ! configures_claude && ! configures_copilot; then
    echo "No agent harness to configure (AGENTBOX_HARNESS=${AGENTBOX_HARNESS:-unset}); nothing to do."
    exit 0
fi

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
        # A file the manifest declares but that can't be downloaded is a broken config, not a reason
        # to apply the rest: fail the whole resolution so the caller reports it.
        curl -fsSL --max-time 20 -o "${dir}/${file}" "${base}/${file}" || {
            echo "ERROR: agent.json declares ${file}, but ${base}/${file} could not be downloaded." >&2
            return 1
        }
    done < <(jq -r '[.systemPrompt, .initialMessage] | map(select(type == "string"))[]' "${dir}/agent.json")
}

CONFIG_DIR=""
if [ -n "${AGENTBOX_CONFIG_DIR:-}" ]; then
    # Explicitly pointed somewhere: never quietly fall back to the network from there.
    if [ ! -f "${AGENTBOX_CONFIG_DIR}/agent.json" ]; then
        echo "ERROR: AGENTBOX_CONFIG_DIR=${AGENTBOX_CONFIG_DIR} has no agent.json." >&2
        exit 1
    fi
    CONFIG_DIR="${AGENTBOX_CONFIG_DIR}"
elif [ -f "${SCRIPT_DIR}/../../agent/agent.json" ]; then
    CONFIG_DIR="$(cd "${SCRIPT_DIR}/../../agent" && pwd)"
elif download_config "${AGENT_CONFIG_URL}" "${WORKDIR}/agent"; then
    CONFIG_DIR="${WORKDIR}/agent"
fi

if [ -z "${CONFIG_DIR}" ]; then
    echo "ERROR: could not read the agent config from ${AGENT_CONFIG_URL} — no harness was configured." >&2
    echo "       Set AGENTBOX_CONFIG_DIR to a local src/agent directory, or AGENTBOX_CONFIG_URL to a" >&2
    echo "       reachable agent.json, and re-run." >&2
    exit 1
fi

echo "--- Agent config: ${CONFIG_DIR} ---"
MANIFEST="${CONFIG_DIR}/agent.json"

# A declared-but-absent payload is a broken config and stops the run. A declared payload that exists
# and is empty is how a knob is turned off deliberately (the marked block is then removed), so that
# stays allowed and resolves to nothing.
resolve_payload() {
    local key="$1" name
    name="$(jq -r --arg k "${key}" '.[$k] // "" | select(type == "string")' "${MANIFEST}" 2>/dev/null)"
    [ -z "${name}" ] && return 0
    if [ ! -f "${CONFIG_DIR}/${name}" ]; then
        echo "ERROR: agent.json declares ${key} as ${name}, which is missing from ${CONFIG_DIR}." >&2
        return 1
    fi
    [ -s "${CONFIG_DIR}/${name}" ] && printf '%s' "${CONFIG_DIR}/${name}"
    return 0
}

SYSTEM_PROMPT_FILE="$(resolve_payload systemPrompt)" || exit 1
INITIAL_MESSAGE_FILE="$(resolve_payload initialMessage)" || exit 1

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
# context. Edit the agent config in TALXIS/tools-agentbox, never this generated copy.
message="$(dirname "$(readlink -f "$0")")/initial-message.md"
[ -s "${message}" ] || exit 0
case "${1:-}" in
    claude) jq -Rs '{hookSpecificOutput: {hookEventName: "SessionStart", additionalContext: .}}' < "${message}" ;;
    *)      jq -Rs '{additionalContext: .}' < "${message}" ;;
esac
HOOK
    # The hook is registered for whichever user runs the harness, which needn't be the user that
    # provisioned the box — so don't leave these at the provisioning umask.
    chmod 0755 "${SHARE_DIR}" "${SESSION_START_SCRIPT}" || return 1
    chmod 0644 "${SHARE_DIR}/initial-message.md" || return 1
}

INITIAL_MESSAGE_READY=1
install_initial_message && INITIAL_MESSAGE_READY=0

# --- Claude Code -------------------------------------------------------------------------------

if configures_claude; then
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
    # session is resumed. Merged into settings rather than replacing them, and any earlier agentbox
    # entry is dropped so re-runs don't stack up duplicates.
    if [ "${INITIAL_MESSAGE_READY}" -eq 0 ]; then
        claude_hook_filter='
            .hooks.SessionStart = (
                ((.hooks.SessionStart // [])
                    | map(select(((.hooks // []) | map(.command // "") | any(contains("agentbox"))) | not)))
                + [{matcher: "startup|resume", hooks: [{type: "command", command: $cmd}]}]
            )'
        merge_json_file "${HOME:-/root}/.claude/settings.json" "${claude_hook_filter}" \
            --arg cmd "${SESSION_START_SCRIPT} claude"

        if claude_sudo_home="$(sudo_user_home)"; then
            merge_json_file "${claude_sudo_home}/.claude/settings.json" "${claude_hook_filter}" \
                --arg cmd "${SESSION_START_SCRIPT} claude"
            restore_sudo_user_ownership "${claude_sudo_home}/.claude"
        fi
    fi
fi

# --- GitHub Copilot CLI ------------------------------------------------------------------------

if configures_copilot; then
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

    # Copilot has no machine-level path for any of this, so write every home that could belong to the
    # user who ends up running it: the invoking user, /etc/skel for users created later, and the user
    # behind sudo (the Copilot cloud sandbox provisions as root and runs the agent as that user).
    copilot_dirs=("${HOME:-/root}/.copilot")
    is_root && copilot_dirs+=("/etc/skel/.copilot")
    if copilot_sudo_home="$(sudo_user_home)"; then
        copilot_dirs+=("${copilot_sudo_home}/.copilot")
    fi

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

    [ -n "${copilot_sudo_home:-}" ] && restore_sudo_user_ownership "${copilot_sudo_home}/.copilot"
fi

exit 0
