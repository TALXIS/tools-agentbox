#!/usr/bin/env bash
# Smoke test for the pac-cli devcontainer feature
set -e

FEATURE_ID="pac-cli"
FEATURE_PATH="src/container/features/${FEATURE_ID}"

echo "=== Smoke test: ${FEATURE_ID} ==="

# Build a test container with the feature
docker build \
    --build-arg VERSION=latest \
    --file - \
    --tag "devcontainer-feature-test-${FEATURE_ID}" \
    . << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:10.0
ARG VERSION=latest
COPY src/container/features/pac-cli/install.sh /tmp/install.sh
RUN chmod +x /tmp/install.sh && \
    _REMOTE_USER=root _REMOTE_USER_HOME=/root VERSION=${VERSION} /tmp/install.sh
EOF

echo "--- pac runs through its /usr/local/bin wrapper, with no PATH set up ---"
docker run --rm "devcontainer-feature-test-${FEATURE_ID}" pac help

echo "--- Checking auto-update flag file exists ---"
docker run --rm \
    "devcontainer-feature-test-${FEATURE_ID}" \
    test -f /etc/devcontainer-pac-cli/auto-update

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "devcontainer-feature-test-${FEATURE_ID}" 2>/dev/null || true
