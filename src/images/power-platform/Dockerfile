FROM mcr.microsoft.com/dotnet/sdk:10.0

LABEL org.opencontainers.image.description="Power Platform dev container image — .NET 10, Node.js 22, Azure CLI, PowerShell, Terraform, GitHub CLI, GitHub Copilot CLI, PAC CLI, TXC CLI, Azure Functions Core Tools"
LABEL org.opencontainers.image.source="https://github.com/TALXIS/tools-agentbox"
LABEL org.opencontainers.image.version="1.0.0"

ENV DEBIAN_FRONTEND=noninteractive

# ── Base system dependencies ──────────────────────────────────────────────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl wget git zip unzip jq ca-certificates gnupg lsb-release \
    apt-transport-https software-properties-common \
    sudo procps \
    && rm -rf /var/lib/apt/lists/*

# ── Node.js 22 ────────────────────────────────────────────────────────────────
RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# ── GitHub CLI ────────────────────────────────────────────────────────────────
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
        | gpg --dearmor -o /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] \
https://cli.github.com/packages stable main" > /etc/apt/sources.list.d/github-cli.list \
    && apt-get update && apt-get install -y gh \
    && rm -rf /var/lib/apt/lists/*

# ── Azure CLI ─────────────────────────────────────────────────────────────────
RUN curl -sL https://aka.ms/InstallAzureCLIDeb | bash \
    && az extension add --name azure-devops --yes \
    && rm -rf /var/lib/apt/lists/* /tmp/*

# ── PowerShell ────────────────────────────────────────────────────────────────
RUN . /etc/os-release \
    && wget -q "https://packages.microsoft.com/config/${ID}/${VERSION_ID}/packages-microsoft-prod.deb" \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && apt-get update && apt-get install -y powershell \
    && rm -rf /var/lib/apt/lists/*

# ── Terraform ─────────────────────────────────────────────────────────────────
RUN curl -fsSL https://apt.releases.hashicorp.com/gpg \
        | gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] \
https://apt.releases.hashicorp.com $(lsb_release -cs) main" > /etc/apt/sources.list.d/hashicorp.list \
    && apt-get update && apt-get install -y terraform \
    && rm -rf /var/lib/apt/lists/*

# ── Azure Functions Core Tools v4 ────────────────────────────────────────────
# Install via npm - the apt repo does not support Ubuntu 24.04 (Noble)
RUN npm install -g azure-functions-core-tools@4 --unsafe-perm true

# ── GitHub Copilot CLI ────────────────────────────────────────────────────────
RUN ARCH=$(dpkg --print-architecture) \
    && [ "${ARCH}" = "amd64" ] && ARCH="x64" || true \
    && curl -fsSL "https://github.com/github/copilot-cli/releases/latest/download/copilot-linux-${ARCH}.tar.gz" \
        | tar -xz -C /usr/local/bin \
    && chmod +x /usr/local/bin/copilot \
    # Enable auto-update on container start (mirrors copilot-cli feature behavior)
    && mkdir -p /etc/devcontainer-copilot-cli \
    && touch /etc/devcontainer-copilot-cli/auto-update

# ── PAC CLI + TXC CLI ────────────────────────────────────────────────────────
# Install to /usr/local/bin so all users can run them without PATH changes
RUN dotnet tool install --tool-path /usr/local/bin Microsoft.PowerApps.CLI.Tool \
    && mkdir -p /etc/devcontainer-pac-cli && touch /etc/devcontainer-pac-cli/auto-update \
    && dotnet tool install --tool-path /usr/local/bin TALXIS.CLI \
    && mkdir -p /etc/devcontainer-txc-cli && touch /etc/devcontainer-txc-cli/auto-update

# ── TXC MCP Server config (GitHub Copilot CLI) ───────────────────────────────
# Place in /etc/skel so it is copied to any new user's home by devcontainer runtime
RUN mkdir -p /etc/skel/.copilot \
    && echo '{"mcpServers":{"TXC":{"type":"stdio","command":"dnx","args":["TALXIS.CLI.MCP","--yes"]}}}' \
        > /etc/skel/.copilot/mcp-config.json


