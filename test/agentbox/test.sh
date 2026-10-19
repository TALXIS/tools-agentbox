#!/usr/bin/env bash
# Smoke test for the agentbox devcontainer feature, with the CLI built from this checkout
set -e

FEATURE_ID="agentbox"

echo "=== Smoke test: ${FEATURE_ID} ==="

# The local package gets a version above any release, so the Feature installs it, not nuget.org's.
docker build \
    --file - \
    --tag "devcontainer-feature-test-${FEATURE_ID}" \
    . << 'EOF2'
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS cli
COPY src/cli/ /src/
RUN dotnet pack /src/TALXIS.DevKit.AgentBox.CLI --configuration Release -p:Version=999.0.0 --output /nupkg

FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=cli /nupkg/ /nupkg/
COPY src/container/features/agentbox/ /tmp/feature/
RUN dotnet nuget add source /nupkg --name local && bash /tmp/feature/install.sh
# A stand-in Copilot CLI that accepts any plugin command, so the create hook has a harness to set up.
RUN printf '#!/bin/sh\necho "[]"\n' > /usr/local/bin/copilot && chmod 755 /usr/local/bin/copilot
EOF2

echo "--- A non-root user's non-login shell runs it ---"
docker run --rm --user app "devcontainer-feature-test-${FEATURE_ID}" \
    env -i PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin HOME=/tmp \
    agentbox prerequisite list --format text

echo "--- The Feature's hooks run, without the '|| true' that keeps a container starting ---"
hooks="$(jq -r '.postCreateCommand, .postStartCommand' "src/container/features/${FEATURE_ID}/devcontainer-feature.json" | sed 's/ || true$//')"
echo "${hooks}"
docker run --rm "devcontainer-feature-test-${FEATURE_ID}" bash -ec "${hooks}
grep -q implement@talxis ~/.copilot/settings.json && echo 'the create hook set up the harness: OK'"

echo "=== ${FEATURE_ID} smoke test PASSED ==="
docker image rm "devcontainer-feature-test-${FEATURE_ID}" 2>/dev/null || true
