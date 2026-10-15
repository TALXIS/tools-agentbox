#!/usr/bin/env bash
# Smoke test for the keyring devcontainer feature
set -e

FEATURE_ID="keyring"

echo "=== Smoke test: ${FEATURE_ID} ==="

docker build \
    --file - \
    --tag "devcontainer-feature-test-${FEATURE_ID}" \
    . << 'EOF2'
FROM ubuntu:24.04
COPY src/container/features/keyring/ /tmp/feature/
RUN bash /tmp/feature/install.sh && apt-get update && apt-get install -y --no-install-recommends procps
EOF2

run() {
    # -e: every assertion in a case counts, not just its last command.
    docker run --rm "devcontainer-feature-test-${FEATURE_ID}" bash -ec "$1"
}

# Each case is a script passed to the container's bash as-is (quoted heredoc, no escaping).
case_script() { cat; }

echo "--- Prints the bus address for eval ---"
run "$(case_script <<'CASE'
test "$(agentbox-keyring-init)" = "export DBUS_SESSION_BUS_ADDRESS='unix:path=/tmp/agentbox-keyring-0/bus'"
CASE
)"

echo "--- Store and read back a secret from a non-login shell ---"
run "$(case_script <<'CASE'
eval "$(agentbox-keyring-init)"
printf x | secret-tool store --label=test agentbox test
test "$(secret-tool lookup agentbox test)" = x
CASE
)"

echo "--- Login and interactive shells get the bus address ---"
run "$(case_script <<'CASE'
bash -lc 'test -n "$DBUS_SESSION_BUS_ADDRESS"'
bash -ic 'test -n "$DBUS_SESSION_BUS_ADDRESS"'
CASE
)"

echo "--- Secret survives killing both daemons (container restart) ---"
run "$(case_script <<'CASE'
eval "$(agentbox-keyring-init)"
printf x | secret-tool store --label=test agentbox test
pkill -x gnome-keyring-d; pkill -x dbus-daemon; sleep 1
rm -rf /tmp/agentbox-keyring-*
eval "$(agentbox-keyring-init)"
test "$(secret-tool lookup agentbox test)" = x
CASE
)"

echo "--- A session bus set up by someone else is passed through untouched ---"
run "$(case_script <<'CASE'
mkdir -p /tmp/other && dbus-daemon --session --address=unix:path=/tmp/other/bus --fork
out="$(DBUS_SESSION_BUS_ADDRESS=unix:path=/tmp/other/bus agentbox-keyring-init)"
test "$out" = "export DBUS_SESSION_BUS_ADDRESS='unix:path=/tmp/other/bus'"
! pgrep -x gnome-keyring-d
CASE
)"

echo "--- Works for a non-root user ---"
run "$(case_script <<'CASE'
useradd -m agentboxtest
su agentboxtest -c 'eval "$(agentbox-keyring-init)"; printf y | secret-tool store --label=t a b; test "$(secret-tool lookup a b)" = y'
CASE
)"

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "devcontainer-feature-test-${FEATURE_ID}" 2>/dev/null || true
