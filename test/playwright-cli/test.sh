#!/usr/bin/env bash
# Smoke test for the playwright-cli devcontainer feature: a fresh install, and a machine that
# already has an older Playwright Chromium (as Claude Code cloud does).
set -e

FEATURE_ID="playwright-cli"
IMAGE="devcontainer-feature-test-${FEATURE_ID}"

echo "=== Smoke test: ${FEATURE_ID} ==="

# Opens a page, reads it and closes it, as the given user, from a shell with no inherited settings.
check_cli() {
    docker run --rm --user "$2" --workdir /tmp "$1" env -i PATH=/usr/local/bin:/usr/bin:/bin HOME=/tmp bash -ec '
        playwright-cli -s=smoke open "data:text/html,<h1>ready</h1>" >/dev/null
        playwright-cli -s=smoke snapshot | grep -q ready
        playwright-cli -s=smoke close >/dev/null'
    echo "playwright-cli as $2: OK"
}

echo "--- Fresh install (the install itself launches the browser as root) ---"
docker build --file - --tag "${IMAGE}" . << 'EOF2'
FROM node:22-bookworm
COPY src/container/features/playwright-cli/ /tmp/playwright-cli/
RUN bash /tmp/playwright-cli/install.sh
EOF2
check_cli "${IMAGE}" node

echo "--- Another user can add a browser build to the shared folder, not remove ours ---"
docker run --rm --user node "${IMAGE}" bash -ec '
    cd /usr/local/share/pw-browsers
    mkdir chromium_headless_shell-0 && touch .links/node
    rm -rf chromium-[0-9]* 2>/dev/null || true
    ls chromium-[0-9]*/INSTALLATION_COMPLETE >/dev/null'
echo "shared browsers folder: OK"

echo "--- An existing Playwright Chromium is used, not downloaded again ---"
docker build --file - --tag "${IMAGE}-existing" . << EOF2
FROM ${IMAGE}
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers
RUN npx -y playwright@1.56.1 install chromium
COPY src/container/features/playwright-cli/ /tmp/playwright-cli/
RUN bash /tmp/playwright-cli/install.sh
EOF2
check_cli "${IMAGE}-existing" root
docker run --rm "${IMAGE}-existing" bash -c 'test "$(ls -d /opt/pw-browsers/chromium-[0-9]*)" = /opt/pw-browsers/chromium-1194'
echo "reused chromium-1194: OK"

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "${IMAGE}" "${IMAGE}-existing" 2>/dev/null || true
