# tools-agentbox

Devcontainer features, templates, and pre-built Docker images for Power Platform / Dataverse developers. Works in GitHub Codespaces, VS Code Dev Containers, and Azure Container Instances.

## Published artifacts

| Artifact | GHCR Reference |
|----------|---------------|
| PAC CLI feature | `ghcr.io/talxis/tools-agentbox/pac-cli:latest` |
| TXC CLI feature | `ghcr.io/talxis/tools-agentbox/txc-cli:latest` |
| Power Platform template | `ghcr.io/talxis/tools-agentbox/power-platform:latest` |
| Pre-built image | `ghcr.io/talxis/tools-agentbox/image:latest` |

## Claude Code cloud environments

[`docs/claude-code-cloud-setup.sh`](docs/claude-code-cloud-setup.sh) is a setup script for an
org-shared [Claude Code cloud environment](https://code.claude.com/docs/en/cloud-environments): paste it
into the environment's setup script field, set network access to Custom and allow `ghcr.io` and
`registry.npmjs.org` in addition to the defaults (some Features also reach out on their own — allow
`aka.ms`, `cli.github.com`, or `packages.microsoft.com` too if a Feature's install fails on one of
those). No changes needed in individual repos. It installs the exact Feature list in
[`devcontainer.features.json`](src/templates/power-platform/.devcontainer/devcontainer.features.json) —
the same one the template below builds from — directly onto the VM instead of into a container, since
Claude Code cloud environments have no Docker daemon to build one in.

The setup script only reruns every ~7 days, so [`docs/claude-code-session-start-hook.json`](docs/claude-code-session-start-hook.json)
adds a `txc`/templates update check on every session: merge its `hooks` into
[Admin Settings > Claude Code > Managed settings](https://claude.ai/admin-settings/claude-code). It's a
no-op outside sessions that already have `txc` installed, so it's safe org-wide.

## Quick start

### Using the template (Codespaces / VS Code)

Create a `.devcontainer/devcontainer.json` in your project:

```json
{
  "name": "Power Platform",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest",
  "customizations": {
    "codespaces": {
      "openFiles": ["README.md"]
    }
  },
  "secrets": {
    "DATAVERSE_ENV_URL": {
      "description": "Your Dataverse environment URL"
    }
  },
  "hostRequirements": {
    "cpus": 2,
    "memory": "8gb"
  }
}
```

Or build from scratch on a dotnet+node capable base image using individual features — copy the
`"features"` object out of
[`devcontainer.features.json`](src/templates/power-platform/.devcontainer/devcontainer.features.json)
(the single source of truth for this list) into your own `devcontainer.json`:

```json
{
  "name": "Power Platform",
  "image": "mcr.microsoft.com/dotnet/sdk:10.0",
  "features": {}
}
```

### Running locally with Docker

```bash
docker pull ghcr.io/talxis/tools-agentbox/image:latest
docker run -it \
  -e DATAVERSE_ENV_URL="https://contoso.crm4.dynamics.com" \
  ghcr.io/talxis/tools-agentbox/image:latest \
  bash
```

## Included tools

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 10.0 | Build Power Platform / Dataverse artifacts |
| Node.js | 22 LTS | Frontend, PCF controls |
| Azure CLI | latest | Manage Azure resources, authenticate |
| PowerShell | latest | Automation, Power Platform pipelines |
| Terraform | latest | Infrastructure as code |
| GitHub CLI | latest | Repo management, GitHub Actions |
| GitHub Copilot CLI | latest (auto-updates) | AI coding assistant in terminal |
| Power Platform CLI (pac) | latest (auto-updates) | pac auth, solution, pcf, plugin |
| TALXIS DevKit CLI (txc) | latest (auto-updates) | Local-first Dataverse development |
| Azure Functions Core Tools | v4 | Local Azure Functions development |
