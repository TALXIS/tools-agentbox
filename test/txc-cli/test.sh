#!/usr/bin/env bash
# Smoke test for the txc-cli devcontainer feature
set -e

FEATURE_ID="txc-cli"

echo "=== Smoke test: ${FEATURE_ID} ==="

docker build \
    --build-arg VERSION=latest \
    --file - \
    --tag "devcontainer-feature-test-${FEATURE_ID}" \
    . << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:10.0
ARG VERSION=latest
ENV PATH="${PATH}:/root/.dotnet/tools"
COPY src/features/txc-cli/install.sh /tmp/install.sh
RUN chmod +x /tmp/install.sh && \
    _REMOTE_USER=root _REMOTE_USER_HOME=/root VERSION=${VERSION} /tmp/install.sh
EOF

echo "--- Checking txc command exists ---"
docker run --rm \
    "devcontainer-feature-test-${FEATURE_ID}" \
    bash -c 'PATH="$PATH:/root/.dotnet/tools" txc --version'

echo "--- Checking auto-update flag file exists ---"
docker run --rm \
    "devcontainer-feature-test-${FEATURE_ID}" \
    test -f /etc/devcontainer-txc-cli/auto-update

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "devcontainer-feature-test-${FEATURE_ID}" 2>/dev/null || true
