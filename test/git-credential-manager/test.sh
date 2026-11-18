#!/usr/bin/env bash
# Smoke test for the git-credential-manager devcontainer feature
set -e

FEATURE_ID="git-credential-manager"

echo "=== Smoke test: ${FEATURE_ID} ==="

docker build \
    --build-arg VERSION=latest \
    --file - \
    --tag "devcontainer-feature-test-${FEATURE_ID}" \
    . << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:10.0
ARG VERSION=latest
COPY src/container/features/keyring/ /tmp/keyring/
COPY src/container/features/git-credential-manager/install.sh /tmp/install.sh
RUN bash /tmp/keyring/install.sh && chmod +x /tmp/install.sh && VERSION=${VERSION} /tmp/install.sh
EOF

run() {
    docker run --rm "$@" "devcontainer-feature-test-${FEATURE_ID}" bash -ec "${cmd}"
}

echo "--- GCM runs through its /usr/local/bin wrapper, with no PATH set up ---"
docker run --rm "devcontainer-feature-test-${FEATURE_ID}" git-credential-manager --version

echo "--- and for a user other than the one it was installed as ---"
docker run --rm --user app "devcontainer-feature-test-${FEATURE_ID}" git-credential-manager --version

echo "--- Git uses the wrapper as its helper, with the Secret Service store ---"
cmd='test "$(git config --get-all credential.helper | tail -n 1)" = /usr/local/bin/git-credential-manager
test "$(git config credential.credentialStore)" = secretservice'
run
run --user app

echo "--- Stores, reads back and erases a credential in the keyring, as a non-root user ---"
cmd='printf "protocol=https\nhost=example.com\nusername=agentbox\npassword=secret\n\n" | git credential approve
out="$(printf "protocol=https\nhost=example.com\nusername=agentbox\n\n" | GCM_INTERACTIVE=never git credential fill)"
echo "$out" | grep -qx password=secret
printf "protocol=https\nhost=example.com\nusername=agentbox\n\n" | git credential reject'
run --user app

echo "--- Checking there is no auto-update flag file ---"
docker run --rm \
    "devcontainer-feature-test-${FEATURE_ID}" \
    test ! -e /etc/devcontainer-git-credential-manager/auto-update

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "devcontainer-feature-test-${FEATURE_ID}" 2>/dev/null || true
