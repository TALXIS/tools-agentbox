#!/usr/bin/env bash
# Smoke test for the pa-cli devcontainer feature (with its keyring dependency)
set -e

FEATURE_ID="pa-cli"

echo "=== Smoke test: ${FEATURE_ID} ==="

docker build \
    --build-arg VERSION=latest \
    --file - \
    --tag "devcontainer-feature-test-${FEATURE_ID}" \
    . << 'EOF2'
FROM node:22-bookworm
ARG VERSION=latest
COPY src/container/features/keyring/ /tmp/keyring/
COPY src/container/features/pa-cli/ /tmp/pa-cli/
RUN bash /tmp/keyring/install.sh && \
    _REMOTE_USER=root VERSION=${VERSION} bash /tmp/pa-cli/install.sh
EOF2

echo "--- pa auth status succeeds with no manual keyring setup ---"
docker run --rm "devcontainer-feature-test-${FEATURE_ID}" \
    bash -c 'pa auth status --json | tee /dev/stderr | grep -q "\"success\": true"'

echo "--- Checking auto-update flag file exists ---"
docker run --rm "devcontainer-feature-test-${FEATURE_ID}" \
    test -f /etc/devcontainer-pa-cli/auto-update

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "devcontainer-feature-test-${FEATURE_ID}" 2>/dev/null || true
