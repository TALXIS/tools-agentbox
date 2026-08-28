#!/usr/bin/env bash
# Asserts that src/agent/ actually reaches every place a harness reads from. Applies the config with
# stub `claude`/`copilot` binaries on PATH, so neither real harness (nor its auth) is needed:
# configure-agent-harness.sh only cares that the binary exists, and every plugin call it makes is
# allowed to fail.
#
# Called by test/agent/test.sh inside a throwaway container. Can also be run directly to verify a
# live AgentBox — it writes to $HOME, /etc/claude-code, /etc/skel and /usr/local/share/agentbox, so
# point HOME at a scratch directory first and expect those system paths to be (re)written:
#
#   HOME="$(mktemp -d)" bash test/agent/assert.sh
set -e

REPO="${REPO:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
CONFIGURE="${REPO}/src/container/scripts/configure-agent-harness.sh"

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

# Stubs go on PATH rather than into /usr/local/bin, so a run on a live AgentBox doesn't shadow the
# harness binaries it actually has installed.
PLUGIN_LOG="${WORK}/plugin-calls.log"
: > "${PLUGIN_LOG}"
mkdir -p "${WORK}/bin"
for bin in claude copilot; do
    cat > "${WORK}/bin/${bin}" <<STUB
#!/bin/bash
echo "${bin} \$*" >> "${PLUGIN_LOG}"
exit 0
STUB
    chmod +x "${WORK}/bin/${bin}"
done
export PATH="${WORK}/bin:${PATH}"

export AGENTBOX_CONFIG_DIR="${REPO}/src/agent"
CONFIG="${AGENTBOX_CONFIG_DIR}"

fail() { echo "FAIL: $1"; exit 1; }
assert_contains() { grep -qF "$2" "$1" || fail "$1 does not contain: $2"; }

bash "${CONFIGURE}"

echo "--- plugins registered on both harnesses ---"
assert_contains "${PLUGIN_LOG}" "claude plugin marketplace add TALXIS/skills"
assert_contains "${PLUGIN_LOG}" "claude plugin install implement@talxis --yes"
assert_contains "${PLUGIN_LOG}" "copilot plugin marketplace add TALXIS/skills"
assert_contains "${PLUGIN_LOG}" "copilot plugin install implement@talxis"
[ "$(jq -r '.enabledPlugins["implement@talxis"]' "${HOME}/.copilot/settings.json")" = "true" ] \
    || fail "implement@talxis not enabled in ~/.copilot/settings.json"
[ "$(jq -r '.extraKnownMarketplaces.talxis.source.repo' "${HOME}/.copilot/settings.json")" = "TALXIS/skills" ] \
    || fail "the talxis marketplace is not declared in ~/.copilot/settings.json"
[ "$(jq -r '.enabledPlugins["implement@talxis"]' /etc/skel/.copilot/settings.json)" = "true" ] \
    || fail "implement@talxis not enabled in /etc/skel/.copilot/settings.json"

echo "--- system prompt reaches both harnesses ---"
FIRST_LINE="$(head -1 "${CONFIG}/system-prompt.md")"
assert_contains /etc/claude-code/CLAUDE.md "${FIRST_LINE}"
assert_contains /etc/claude-code/CLAUDE.md "BEGIN AGENTBOX"
assert_contains "${HOME}/.copilot/copilot-instructions.md" "${FIRST_LINE}"
assert_contains /etc/skel/.copilot/copilot-instructions.md "${FIRST_LINE}"

echo "--- initial message is wired as a session-start hook on both harnesses ---"
assert_contains /usr/local/share/agentbox/initial-message.md "$(head -1 "${CONFIG}/initial-message.md")"
CLAUDE_HOOK="$(jq -r '.hooks.SessionStart[0].hooks[0].command' "${HOME}/.claude/settings.json")"
case "${CLAUDE_HOOK}" in
    */agentbox/session-start.sh\ claude) ;;
    *) fail "unexpected Claude SessionStart hook command: ${CLAUDE_HOOK}" ;;
esac
[ "$(jq -r '.version' "${HOME}/.copilot/hooks/agentbox.json")" = "1" ] \
    || fail "~/.copilot/hooks/agentbox.json is not a version 1 hook file"
COPILOT_HOOK="$(jq -r '.hooks.sessionStart[0].bash' "${HOME}/.copilot/hooks/agentbox.json")"
case "${COPILOT_HOOK}" in
    */agentbox/session-start.sh\ copilot) ;;
    *) fail "unexpected Copilot sessionStart hook command: ${COPILOT_HOOK}" ;;
esac

echo "--- the hook emits the JSON shape each harness expects ---"
/usr/local/share/agentbox/session-start.sh claude \
    | jq -e '.hookSpecificOutput.hookEventName == "SessionStart" and (.hookSpecificOutput.additionalContext | length) > 0' >/dev/null \
    || fail "the Claude hook did not emit hookSpecificOutput.additionalContext"
/usr/local/share/agentbox/session-start.sh copilot \
    | jq -e '(.additionalContext | length) > 0' >/dev/null \
    || fail "the Copilot hook did not emit additionalContext"

echo "--- re-running is idempotent and keeps content the developer added ---"
printf '\n# My own notes\n' >> "${HOME}/.copilot/copilot-instructions.md"
bash "${CONFIGURE}"
[ "$(grep -cF "BEGIN AGENTBOX" "${HOME}/.copilot/copilot-instructions.md")" = "1" ] \
    || fail "the AGENTBOX block was duplicated in ~/.copilot/copilot-instructions.md"
assert_contains "${HOME}/.copilot/copilot-instructions.md" "# My own notes"
[ "$(jq -r '.hooks.SessionStart | length' "${HOME}/.claude/settings.json")" = "1" ] \
    || fail "the Claude SessionStart hook was duplicated on re-run"

echo "--- emptying system-prompt.md removes the block, not the developer's file ---"
mkdir -p "${WORK}/empty-config"
cp "${CONFIG}/agent.json" "${CONFIG}/initial-message.md" "${WORK}/empty-config/"
: > "${WORK}/empty-config/system-prompt.md"
AGENTBOX_CONFIG_DIR="${WORK}/empty-config" bash "${CONFIGURE}"
grep -qF "BEGIN AGENTBOX" "${HOME}/.copilot/copilot-instructions.md" \
    && fail "the AGENTBOX block survived an emptied system-prompt.md"
assert_contains "${HOME}/.copilot/copilot-instructions.md" "# My own notes"
[ -f /etc/claude-code/CLAUDE.md ] \
    && fail "/etc/claude-code/CLAUDE.md should be gone once the block it only held is removed"

echo "--- AGENTBOX_HARNESS=none touches nothing ---"
rm -rf "${HOME}/.claude" "${HOME}/.copilot" /etc/claude-code
AGENTBOX_HARNESS=none bash "${CONFIGURE}"
[ -e "${HOME}/.claude/settings.json" ] && fail "AGENTBOX_HARNESS=none wrote Claude settings"
[ -e /etc/claude-code/CLAUDE.md ] && fail "AGENTBOX_HARNESS=none wrote the managed CLAUDE.md"
[ -e "${HOME}/.copilot/copilot-instructions.md" ] && fail "AGENTBOX_HARNESS=none wrote Copilot instructions"

echo "=== agent config assertions PASSED ==="
