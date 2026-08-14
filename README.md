# tools-agentbox

Devcontainer features, templates, and pre-built Docker images for Power Platform / Dataverse developers.

**Don't run coding agents directly on your own workstation.** They execute arbitrary commands, which is a
real security exposure, and heavy agent workloads wear on a machine over time. Run them in one of the
isolated environments below instead — see GitHub's own take on why:
[About cloud and local sandboxes](https://docs.github.com/en/copilot/concepts/about-cloud-and-local-sandboxes).

## Choose your environment

### 1. GitHub Codespaces

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

Or build your own `devcontainer.json` from individual features listed in
[`devcontainer.features.json`](src/templates/power-platform/.devcontainer/devcontainer.features.json) —
the single source of truth for this toolchain's tool/version list.

### 2. GitHub Copilot cloud sandbox

Copilot's cloud agent environment doesn't use `devcontainer.json` — customize it with
[`.github/workflows/copilot-setup-steps.yml`](.github/workflows/copilot-setup-steps.yml) instead, which
runs the same setup script as the Claude Code cloud environment below. No separate tool list to maintain.

### 3. Claude Code cloud environment

[`src/scripts/install-features.sh`](src/scripts/install-features.sh) sets up an org-shared
[Claude Code cloud environment](https://code.claude.com/docs/en/cloud-environments). Paste this into the
environment's setup script field instead of the file itself — it always fetches and runs whatever is
currently on `master`, so the console field never needs updating when the script changes:

```bash
curl -fsSL https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/scripts/install-features.sh \
  -o /tmp/agentbox-setup.sh && bash /tmp/agentbox-setup.sh || echo "agentbox setup failed to download or run" >&2
```

Also set two more things on the environment itself:

- **Environment variables**: `DOTNET_ROOT=/usr/local/dotnet/current` — required, not optional; without
  it, `dotnet`/`pac`/`txc` fail for any caller that doesn't go through the setup script's own wrapper
  scripts (e.g. a PATH order copied from a real devcontainer, where `DOTNET_ROOT` is already a
  container-wide `ENV`).
- **Network access**: Custom, with the defaults included, plus `cli.github.com`.

It installs the exact Feature list in [`devcontainer.features.json`](src/templates/power-platform/.devcontainer/devcontainer.features.json) —
the same one Codespaces builds from — directly onto the VM instead of into a container, since Claude Code
cloud environments have no Docker daemon. No changes needed in individual repos.

The setup script only reruns every ~7 days, so [`src/claude-code/session-start-hook.json`](src/claude-code/session-start-hook.json)
adds a `txc`/templates update check on every session: merge its `hooks` into
[Admin Settings > Claude Code > Managed settings](https://claude.ai/admin-settings/claude-code). It's a
no-op outside sessions that already have `txc` installed, so it's safe org-wide.

### 4. Docker

```bash
docker pull ghcr.io/talxis/tools-agentbox/image:latest
docker run -it \
  -e DATAVERSE_ENV_URL="https://contoso.crm4.dynamics.com" \
  ghcr.io/talxis/tools-agentbox/image:latest \
  bash
```

## Published artifacts

| Artifact | GHCR Reference |
|----------|---------------|
| PAC CLI feature | `ghcr.io/talxis/tools-agentbox/pac-cli:latest` |
| TXC CLI feature | `ghcr.io/talxis/tools-agentbox/txc-cli:latest` |
| Power Platform template | `ghcr.io/talxis/tools-agentbox/power-platform:latest` |
| Pre-built image | `ghcr.io/talxis/tools-agentbox/image:latest` |

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
