FROM ubuntu:24.04

# Set by publish-image.yaml from the GitHub Release tag that triggered the build - never hardcode
# a version string here, it will just go stale the way the old one did.
ARG IMAGE_VERSION=0.0.0-dev

LABEL org.opencontainers.image.description="Power Platform / Dataverse development environment"
LABEL org.opencontainers.image.source="https://github.com/TALXIS/tools-agentbox"
LABEL org.opencontainers.image.version="${IMAGE_VERSION}"

ENV DEBIAN_FRONTEND=noninteractive

# ── Base system dependencies ──────────────────────────────────────────────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl wget git zip unzip jq ca-certificates gnupg lsb-release \
    ripgrep fd-find bat delta fzf tree xdg-utils less file \
    libsecret-1-0 gnome-keyring \
    apt-transport-https software-properties-common \
    sudo procps \
    && rm -rf /var/lib/apt/lists/*

# ── Toolchain: dotnet, node, az, pwsh, terraform, gh, copilot-cli, func, pac, txc ───────────────
# Single place this is defined: src/container/scripts/install-features.sh, reading
# src/container/templates/power-platform/.devcontainer/devcontainer.features.json — the same script
# the Claude Code cloud environment and the Copilot cloud sandbox already run. AGENTBOX_HARNESS=none
# skips the TALXIS/skills plugin registration here; Codespaces' own postCreateCommand handles that
# once, at container creation, as the container's real user — baking it in at build time (as root)
# wouldn't reliably reach that user. CACHEBUST forces this layer to actually re-run on every build
# instead of silently reusing a stale cached layer: most Feature versions resolve to "latest", and
# Docker's cache is keyed on instruction text, not on what a re-run would actually fetch.
ARG CACHEBUST=0
COPY src/container/scripts/install-features.sh /tmp/install-features.sh
RUN AGENTBOX_HARNESS=none CACHEBUST=${CACHEBUST} bash /tmp/install-features.sh \
    && rm /tmp/install-features.sh

ENV DOTNET_ROOT=/usr/local/dotnet/current
