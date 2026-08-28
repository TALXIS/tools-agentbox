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

echo "--- the shared hook is readable by users other than the one that provisioned ---"
SHARE_DIR="$(dirname "$(jq -r '.hooks.SessionStart[0].hooks[0].command' "${HOME}/.claude/settings.json" | cut -d' ' -f1)")"
[ "$(stat -c '%a' "${SHARE_DIR}")" = "755" ] || fail "${SHARE_DIR} is not mode 755"
[ "$(stat -c '%a' "${SHARE_DIR}/session-start.sh")" = "755" ] || fail "session-start.sh is not mode 755"
[ "$(stat -c '%a' "${SHARE_DIR}/initial-message.md")" = "644" ] || fail "initial-message.md is not mode 644"

# The Copilot cloud sandbox provisions as root under sudo and runs the agent as SUDO_USER, so the
# user-level files have to reach that home too. Env-gated: test.sh creates the user, and a direct run
# against a live box skips it rather than writing into someone's home.
if [ -n "${AGENTBOX_TEST_SUDO_USER:-}" ]; then
    echo "--- user-level config is mirrored to the user behind sudo ---"
    SUDO_HOME="$(getent passwd "${AGENTBOX_TEST_SUDO_USER}" | cut -d: -f6)"
    [ -n "${SUDO_HOME}" ] || fail "no home directory for ${AGENTBOX_TEST_SUDO_USER}"
    rm -rf "${SUDO_HOME}/.copilot" "${SUDO_HOME}/.claude"
    SUDO_USER="${AGENTBOX_TEST_SUDO_USER}" bash "${CONFIGURE}"
    assert_contains "${SUDO_HOME}/.copilot/copilot-instructions.md" "${FIRST_LINE}"
    [ "$(jq -r '.enabledPlugins["implement@talxis"]' "${SUDO_HOME}/.copilot/settings.json")" = "true" ] \
        || fail "implement@talxis not enabled in the sudo user's ~/.copilot/settings.json"
    [ -f "${SUDO_HOME}/.copilot/hooks/agentbox.json" ] \
        || fail "no sessionStart hook in the sudo user's ~/.copilot/hooks/"
    [ "$(jq -r '.hooks.SessionStart | length' "${SUDO_HOME}/.claude/settings.json")" = "1" ] \
        || fail "no SessionStart hook in the sudo user's ~/.claude/settings.json"
    [ "$(stat -c '%U' "${SUDO_HOME}/.copilot/copilot-instructions.md")" = "${AGENTBOX_TEST_SUDO_USER}" ] \
        || fail "the sudo user's copilot-instructions.md is not owned by them"
fi

echo "--- an unreadable config fails the run instead of half-configuring ---"
rm -rf "${HOME}/.claude" "${HOME}/.copilot" /etc/claude-code
# A copy outside the repo, so the script can't find src/agent/ as a sibling of itself, with
# AGENTBOX_CONFIG_DIR unset and a URL on loopback port 1 that fails immediately (no network needed).
STANDALONE="${WORK}/configure-standalone.sh"
cp "${CONFIGURE}" "${STANDALONE}"
env -u AGENTBOX_CONFIG_DIR AGENTBOX_CONFIG_URL="http://127.0.0.1:1/agent.json" bash "${STANDALONE}" \
    && fail "the script exited 0 with no readable config"
[ -e "${HOME}/.claude/settings.json" ] && fail "a failed run still wrote Claude settings"
[ -e "${HOME}/.copilot/copilot-instructions.md" ] && fail "a failed run still wrote Copilot instructions"
[ -e /etc/claude-code/CLAUDE.md ] && fail "a failed run still wrote the managed CLAUDE.md"

echo "--- AGENTBOX_HARNESS=none touches nothing, and needs no config ---"
rm -rf "${HOME}/.claude" "${HOME}/.copilot" /etc/claude-code
env -u AGENTBOX_CONFIG_DIR AGENTBOX_HARNESS=none AGENTBOX_CONFIG_URL="http://127.0.0.1:1/agent.json" \
    bash "${STANDALONE}" || fail "AGENTBOX_HARNESS=none should succeed without reading the config"
[ -e "${HOME}/.claude/settings.json" ] && fail "AGENTBOX_HARNESS=none wrote Claude settings"
[ -e /etc/claude-code/CLAUDE.md ] && fail "AGENTBOX_HARNESS=none wrote the managed CLAUDE.md"
[ -e "${HOME}/.copilot/copilot-instructions.md" ] && fail "AGENTBOX_HARNESS=none wrote Copilot instructions"

echo "=== agent config assertions PASSED ==="
