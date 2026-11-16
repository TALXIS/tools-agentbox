# This repo's Features. CI passes them (--build-context agentbox-features=src/container/features and
# AGENTBOX_FEATURES_FROM=/tmp/agentbox-features), so the agentbox lockfile and the image both cover a
# pull request's Feature changes; without them, the pinned, published Features are used.
FROM scratch AS agentbox-features

# ── The agentbox package ──────────────────────────────────────────────────────
# Packed from this checkout: build/pack.sh pins the Features into its lockfile, then packs. On its
# own (docker buildx build --target agentbox-cli --output type=local,dest=<dir> .) it is how CI and
# releases make the nupkg. publish.yaml passes the one it put on nuget.org instead
# (--build-context agentbox-cli=<dir>), so the image installs exactly that package.
# The nupkg is the same on every platform, so it's packed once, natively.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS agentbox-cli-build
RUN apt-get update && apt-get install -y --no-install-recommends jq nodejs npm \
    && rm -rf /var/lib/apt/lists/*
# Above any release by default, so this build wins wherever nuget.org's would.
ARG AGENTBOX_VERSION=999.0.0
ARG AGENTBOX_FEATURES_FROM=
# The lockfile pins the Features' current digests, which Docker's cache can't tell have moved.
ARG CACHEBUST=0
WORKDIR /repo
COPY src/container/agentbox-features.json src/container/
COPY src/cli src/cli
RUN --mount=type=bind,from=agentbox-features,target=/tmp/agentbox-features \
    --mount=type=cache,target=/root/.nuget/packages \
    CACHEBUST=${CACHEBUST} DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    bash src/cli/build/pack.sh "${AGENTBOX_VERSION}" /out

FROM scratch AS agentbox-cli
COPY --from=agentbox-cli-build /out/*.nupkg /

FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

# ── Base system dependencies ──────────────────────────────────────────────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl wget git zip unzip jq ca-certificates gnupg lsb-release libicu74 \
    ripgrep fd-find bat git-delta fzf tree xdg-utils less file \
    apt-transport-https software-properties-common \
    sudo procps \
    && rm -rf /var/lib/apt/lists/*

# ── The AgentBox tools ────────────────────────────────────────────────────────
# The same setup script Claude Code cloud and the Copilot cloud agent run. The harnesses are set up
# when a container is created, as its real user (the agentbox Feature's hook, in the image label).
# CACHEBUST makes this layer re-run on every release: most tools follow their latest release, and
# Docker's cache can't tell.
ARG CACHEBUST=0
ARG AGENTBOX_FEATURES_FROM=
COPY src/container/scripts/install-features.sh /tmp/install-features.sh
RUN --mount=type=bind,from=agentbox-cli,target=/tmp/agentbox-cli \
    --mount=type=bind,from=agentbox-features,target=/tmp/agentbox-features \
    CACHEBUST=${CACHEBUST} bash /tmp/install-features.sh --no-harness --source /tmp/agentbox-cli \
    && rm -rf /tmp/install-features.sh /root/.nuget/packages /root/.npm /usr/local/share/nvm/.cache /var/lib/apt/lists/*

ENV DOTNET_ROOT=/usr/local/dotnet/current
# Where the playwright-cli Feature put the browser, for every Playwright on the same version (.NET tests too).
ENV PLAYWRIGHT_BROWSERS_PATH=/usr/local/share/pw-browsers

# Set by publish.yaml from the release tag. Last, so a new version doesn't invalidate the layers above.
ARG IMAGE_VERSION=0.0.0-dev
LABEL org.opencontainers.image.description="Power Platform / Dataverse development environment"
LABEL org.opencontainers.image.source="https://github.com/TALXIS/tools-agentbox"
LABEL org.opencontainers.image.version="${IMAGE_VERSION}"
