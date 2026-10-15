#!/usr/bin/env bash
# Smoke test for the secret-service devcontainer feature
set -e

FEATURE_ID="secret-service"

echo "=== Smoke test: ${FEATURE_ID} ==="

docker build \
    --file - \
    --tag "devcontainer-feature-test-${FEATURE_ID}" \
    . << 'EOF2'
FROM ubuntu:24.04
COPY src/container/features/secret-service/ /tmp/feature/
RUN bash /tmp/feature/install.sh
EOF2

run() {
    docker run --rm "devcontainer-feature-test-${FEATURE_ID}" bash -c "$1"
}

echo "--- Store and read back a secret from a non-login shell ---"
run '. /etc/profile.d/agentbox-keyring.sh
     printf x | secret-tool store --label=test agentbox test
     test "$(secret-tool lookup agentbox test)" = x'

echo "--- Secret survives killing both daemons (container restart) ---"
run '. /etc/profile.d/agentbox-keyring.sh
     printf x | secret-tool store --label=test agentbox test
     pkill -x gnome-keyring-d; pkill -x dbus-daemon; sleep 1
     rm -rf /tmp/agentbox-dbus-*
     . /etc/profile.d/agentbox-keyring.sh
     test "$(secret-tool lookup agentbox test)" = x'

echo "--- Works for a non-root user ---"
run 'useradd -m agentboxtest
     su agentboxtest -c ". /etc/profile.d/agentbox-keyring.sh; printf y | secret-tool store --label=t a b; test \"\$(secret-tool lookup a b)\" = y"'

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "devcontainer-feature-test-${FEATURE_ID}" 2>/dev/null || true
