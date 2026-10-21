#!/usr/bin/env bash
# Installs Playwright's CLI for coding agents (`playwright cli`, from the playwright npm package)
# behind /usr/local/bin/playwright-cli, with a browser it can launch.
#
# Each Playwright release launches only its own browser build, so the version is pinned; .NET tests
# on the same Microsoft.Playwright version find the same browser. A Playwright Chromium already in
# PLAYWRIGHT_BROWSERS_PATH (Claude Code cloud ships one) is used when it launches, instead of
# downloading another; when it doesn't, this machine is left as it was.
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

echo "Installing playwright@${PLAYWRIGHT_VERSION} into ${PREFIX}"
mkdir -p "${PREFIX}"
npm install -g --prefix "${PREFIX}" "playwright@${PLAYWRIGHT_VERSION}"
playwright() { PLAYWRIGHT_BROWSERS_PATH="${BROWSERS}" PLAYWRIGHT_SKIP_BROWSER_GC=1 "${PREFIX}/bin/playwright" "$@"; }

# $1: the browser executable to use, if not the build this Playwright pins.
write_wrapper() {
    local executable=""
    [ -z "$1" ] || executable="export PLAYWRIGHT_MCP_EXECUTABLE_PATH=\"\${PLAYWRIGHT_MCP_EXECUTABLE_PATH:-$1}\""
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

# Opens a page and closes it, from a scratch directory (open saves a snapshot there).
launches() {
    local dir status=0
    dir="$(mktemp -d)"
    (cd "${dir}" && timeout 120 "${WRAPPER}" -s=feature-check open about:blank >/dev/null) || status=$?
    (cd "${dir}" && "${WRAPPER}" -s=feature-check close >/dev/null 2>&1) || true
    rm -rf "${dir}"
    return "${status}"
}

pinned="$(playwright install --dry-run chromium | sed -n 's/^ *Install location: *//p' | grep '/chromium-[0-9]*$' | head -1)"
existing="$(ls -d "${BROWSERS}"/chromium-[0-9]*/chrome-linux*/chrome 2>/dev/null | sort -V | tail -1 || true)"

if [ -f "${pinned}/INSTALLATION_COMPLETE" ]; then
    echo "The browser for playwright ${PLAYWRIGHT_VERSION} is already in ${BROWSERS}"
    write_wrapper ""
elif [ -n "${existing}" ]; then
    echo "Using the Playwright Chromium already at ${existing}"
    write_wrapper "${existing}"
else
    echo "Installing Chromium and its system libraries into ${BROWSERS}"
    playwright install --with-deps chromium
    rm -rf /var/lib/apt/lists/*
    # Every user launches it; any user can add the build their own Playwright version needs, and
    # the sticky bit keeps them from deleting builds they didn't install.
    mkdir -p "${BROWSERS}/.links"
    chmod -R a+rX "${BROWSERS}"
    chmod 1777 "${BROWSERS}" "${BROWSERS}/.links"
    write_wrapper ""
fi

if ! launches; then
    if [ -n "${existing}" ] && [ ! -f "${pinned}/INSTALLATION_COMPLETE" ]; then
        echo "ERROR: playwright-cli can't launch ${existing}. Leaving this machine's Playwright as it was."
        rm -rf "${PREFIX}" "${WRAPPER}"
    else
        echo "ERROR: playwright-cli can't launch its browser."
    fi
    exit 1
fi
echo "Playwright CLI installed: playwright ${PLAYWRIGHT_VERSION}, browsers in ${BROWSERS}"
echo "Done!"
