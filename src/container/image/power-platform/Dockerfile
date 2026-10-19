# The agentbox package to install. publish.yaml and the image test pass the one they just packed
# (--build-context agentbox-cli=<dir>); without it, agentbox comes from nuget.org.
FROM scratch AS agentbox-cli
# The image test also passes this repo's Features (--build-context agentbox-features=<dir> and
# AGENTBOX_FEATURES_FROM=/tmp/agentbox-features), so it covers a pull request's Feature changes.
FROM scratch AS agentbox-features

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

# Set by publish.yaml from the release tag. Last, so a new version doesn't invalidate the layers above.
ARG IMAGE_VERSION=0.0.0-dev
LABEL org.opencontainers.image.description="Power Platform / Dataverse development environment"
LABEL org.opencontainers.image.source="https://github.com/TALXIS/tools-agentbox"
LABEL org.opencontainers.image.version="${IMAGE_VERSION}"
