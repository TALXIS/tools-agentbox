#!/usr/bin/env bash
# Installs Playwright's CLI for agents (`playwright cli`) as /usr/local/bin/playwright-cli, with a
# browser it can launch. The version is pinned: each Playwright release launches only its own
# browser build, which .NET tests on the same Microsoft.Playwright version share.
set -e

PLAYWRIGHT_VERSION="${VERSION:-"1.63.0"}"
PREFIX="/usr/local/lib/playwright-cli"
BROWSERS="${PLAYWRIGHT_BROWSERS_PATH:-/usr/local/share/pw-browsers}"
WRAPPER="/usr/local/bin/playwright-cli"

if [ "$(id -u)" -ne 0 ]; then
    echo 'Script must be run as root. Use sudo, su, or add "USER root" to your Dockerfile before running this script.'
    exit 1
fi

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
    echo "ERROR: Node.js 22 or newer is required. Add the node Feature before playwright-cli."
    exit 1
fi

node_major="$(node -p 'process.versions.node.split(".")[0]')"
if [ "${node_major}" -lt 22 ]; then
    echo "ERROR: playwright-cli needs Node.js 22 or newer, found $(node --version)."
    exit 1
fi

exists() { [ -e "$1" ] || [ -L "$1" ]; } # a link counts, even a broken one

# Set aside what's there now and put it back if anything fails, so a failed attempt changes nothing.
# Moving the command also means we never write through a link into another package.
previous="$(mktemp -d "${PREFIX}.previous.XXXXXX")"
if exists "${PREFIX}"; then mv "${PREFIX}" "${previous}/prefix"; fi
if exists "${WRAPPER}"; then mv "${WRAPPER}" "${previous}/wrapper"; fi
finish() {
    if [ "$1" -ne 0 ]; then
        echo "ERROR: playwright-cli isn't working. Leaving this machine's Playwright as it was."
        rm -rf "${PREFIX}" "${WRAPPER}"
        if exists "${previous}/prefix"; then mv "${previous}/prefix" "${PREFIX}"; fi
        if exists "${previous}/wrapper"; then mv "${previous}/wrapper" "${WRAPPER}"; fi
    fi
    rm -rf "${previous}"
}
trap 'finish $?' EXIT

echo "Installing playwright@${PLAYWRIGHT_VERSION} into ${PREFIX}"
npm install -g --prefix "${PREFIX}" "playwright@${PLAYWRIGHT_VERSION}"
playwright() { PLAYWRIGHT_BROWSERS_PATH="${BROWSERS}" PLAYWRIGHT_SKIP_BROWSER_GC=1 "${PREFIX}/bin/playwright" "$@"; }

# $1: a browser to use instead of the build this version pins (empty for that build).
write_wrapper() {
    local executable=""
    if [ -n "$1" ]; then
        executable="export PLAYWRIGHT_MCP_EXECUTABLE_PATH=\"\${PLAYWRIGHT_MCP_EXECUTABLE_PATH:-$1}\""
    fi
    cat > "${WRAPPER}" <<WRAPPER
#!/bin/bash
# Installed by the playwright-cli devcontainer Feature (TALXIS/tools-agentbox).
export USER="\${USER:-\$(id -un)}" # the CLI keys its sockets on it; docker exec leaves it unset
export NO_UPDATE_NOTIFIER=1
export PLAYWRIGHT_BROWSERS_PATH="\${PLAYWRIGHT_BROWSERS_PATH:-${BROWSERS}}"
export PLAYWRIGHT_MCP_BROWSER="\${PLAYWRIGHT_MCP_BROWSER:-chromium}" # not the system Google Chrome
${executable}
exec "${PREFIX}/bin/playwright" cli "\$@"
WRAPPER
    chmod 755 "${WRAPPER}"
}

# Opens a page and closes it, with a fresh profile (a saved one may be in use elsewhere).
launches() {
    local dir status=0
    dir="$(mktemp -d)" # open saves a snapshot in the current directory
    (cd "${dir}" && timeout 120 env -u PLAYWRIGHT_MCP_USER_DATA_DIR "${WRAPPER}" -s=feature-check open about:blank >/dev/null) || status=$?
    (cd "${dir}" && "${WRAPPER}" -s=feature-check close >/dev/null 2>&1) || true
    rm -rf "${dir}"
    return "${status}"
}

# The browser, in order of preference: the pinned build if it's there; another Playwright Chromium
# that's there (Claude Code cloud ships one); else download the pinned build.
pinned="$(playwright install --dry-run chromium | sed -n 's/^ *Install location: *//p' | grep '/chromium-[0-9]*$' | head -1)"
existing="$(ls -d "${BROWSERS}"/chromium-[0-9]*/chrome-linux*/chrome 2>/dev/null | sort -V | tail -1 || true)"

if [ -f "${pinned}/INSTALLATION_COMPLETE" ]; then
    echo "The browser for playwright ${PLAYWRIGHT_VERSION} is already in ${BROWSERS}"
    playwright install chromium # adds the headless shell and ffmpeg if they're missing
    write_wrapper ""
elif [ -n "${existing}" ]; then
    echo "Using the Playwright Chromium already at ${existing}"
    write_wrapper "${existing}"
else
    echo "Installing Chromium and its system libraries into ${BROWSERS}"
    playwright install --with-deps chromium
    rm -rf /var/lib/apt/lists/*
    # Any user can launch it and add the build their own Playwright needs; the sticky bit stops
    # them deleting builds they didn't install.
    mkdir -p "${BROWSERS}/.links"
    chmod -R a+rX "${BROWSERS}"
    chmod 1777 "${BROWSERS}" "${BROWSERS}/.links"
    write_wrapper ""
fi

launches
echo "Playwright CLI installed: playwright ${PLAYWRIGHT_VERSION}, browsers in ${BROWSERS}"
echo "Done!"
