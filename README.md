# tools-agentbox

Devcontainer features, templates, and pre-built Docker images for Power Platform / Dataverse developers. Works in GitHub Codespaces, VS Code Dev Containers, and Azure Container Instances.

## Repository structure

```
src/
├── features/
│   ├── pac-cli/          # Power Platform CLI (pac) devcontainer feature
│   └── txc-cli/          # TALXIS DevKit CLI (txc) devcontainer feature
├── templates/
│   └── power-platform/   # Full Power Platform devcontainer template
└── images/
    └── power-platform/   # Pre-built Docker image (Dockerfile)
test/
├── pac-cli/              # Smoke tests for pac-cli feature
├── txc-cli/              # Smoke tests for txc-cli feature
└── power-platform/       # Smoke tests for the pre-built image
.github/
└── workflows/
    ├── release-features.yaml   # Publish features to GHCR
    ├── release-templates.yaml  # Publish templates to GHCR
    ├── build-image.yaml        # Build & push pre-built image (weekly + on change)
    └── test-pr.yaml            # CI: smoke-test changed features/templates on PRs
```

## Published artifacts

| Artifact | GHCR Reference |
|----------|---------------|
| PAC CLI feature | `ghcr.io/talxis/tools-agentbox/pac-cli:latest` |
| TXC CLI feature | `ghcr.io/talxis/tools-agentbox/txc-cli:latest` |
| Power Platform template | `ghcr.io/talxis/tools-agentbox/power-platform:latest` |
| Pre-built image | `ghcr.io/talxis/tools-agentbox/image:latest` |

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

Or use individual features:

```json
{
  "name": "Power Platform",
  "image": "mcr.microsoft.com/dotnet/sdk:10.0",
  "features": {
    "ghcr.io/devcontainers/features/node:1": { "version": "22" },
    "ghcr.io/devcontainers/features/azure-cli:1": { "extensions": "azure-devops" },
    "ghcr.io/devcontainers/features/powershell:1": {},
    "ghcr.io/devcontainers/features/terraform:1": {},
    "ghcr.io/devcontainers/features/github-cli:1": {},
    "ghcr.io/devcontainers/features/copilot-cli:1": {},
    "ghcr.io/jlaundry/devcontainer-features/azure-functions-core-tools:1": {},
    "ghcr.io/talxis/tools-agentbox/pac-cli:1": {},
    "ghcr.io/talxis/tools-agentbox/txc-cli:1": {}
  }
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

## Auto-update behavior

`pac`, `txc`, and `copilot` auto-update on every container start. This means:

- Workshop participants always have the latest tooling without manual intervention
- No need to rebuild the image when a new version ships
- The pre-built image is rebuilt weekly (Monday 03:00 UTC) to keep base layers fresh

## First-time setup after starting a Codespace

Connect to your Power Platform environment:

```bash
# Option A: Power Platform CLI
pac auth create --url $DATAVERSE_ENV_URL

# Option B: TALXIS DevKit CLI
txc config profile create --url $DATAVERSE_ENV_URL
```

## GitHub Actions setup

For GHCR publishing to work, ensure **Allow GitHub Actions to create and approve pull requests** is enabled:

> Repository → Settings → Actions → General → Workflow permissions → ✅ Allow GitHub Actions to create and approve pull requests

After the first successful `release-features.yaml` run, make the packages public:

```
https://github.com/orgs/TALXIS/packages/container/tools-agentbox%2Fpac-cli/settings
https://github.com/orgs/TALXIS/packages/container/tools-agentbox%2Ftxc-cli/settings
https://github.com/orgs/TALXIS/packages/container/tools-agentbox%2Fimage/settings
```

## License

MIT — TALXIS / NETWORG s.r.o.
