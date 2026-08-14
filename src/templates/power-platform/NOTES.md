# Power Platform Dev Container Template

This template provides a complete, ready-to-use development environment for Power Platform / Dataverse engineers. It works in GitHub Codespaces, VS Code Dev Containers, and can also be used to build a Docker image for Azure Container Instances.

## What's included

| Tool | Purpose |
|------|---------|
| .NET 10 SDK | Build Power Platform/Dataverse artifacts with dotnet SDK |
| Node.js 22 | Frontend development, PCF controls |
| Azure CLI | Manage Azure resources, authenticate to Dataverse |
| PowerShell (pwsh) | Automation scripts, Power Platform pipelines |
| Terraform | Infrastructure as code |
| GitHub CLI (gh) | Repo management, GitHub Actions |
| GitHub Copilot CLI | AI pair programming in the terminal |
| Power Platform CLI (pac) | pac auth, pac solution, pac pcf, pac plugin |
| TALXIS DevKit CLI (txc) | Local-first Dataverse scaffolding and deployment |
| Azure Functions Core Tools | Develop and test Azure Functions locally |

## Getting started in Codespaces

1. Click **Use this template** or open the repo in Codespaces
2. When prompted for secrets, provide your Dataverse environment details (optional)
3. Run `pac auth create` or `txc config profile create --url https://your-env.crm4.dynamics.com/` to connect to an environment

## Recommended secrets

Set these in your Codespace (or personal Codespaces settings) for a connected experience:

| Secret | Description |
|--------|-------------|
| `DATAVERSE_ENV_URL` | Your Dataverse environment URL, e.g. `https://contoso.crm4.dynamics.com` |
| `AZURE_TENANT_ID` | Azure Active Directory tenant ID |
| `GH_TOKEN` | GitHub personal access token (for private repos, packages) |
| `AZURE_DEVOPS_EXT_PAT` | Azure DevOps PAT (for ADO integration) |

## Pre-built image

For faster Codespace startup, use the pre-built image which has all tools baked in:

```json
{
  "image": "ghcr.io/talxis/tools-agentbox/image:latest"
}
```

## Auto-update behavior

`pac` and `txc` auto-update to the latest version on every container start. This ensures workshop participants always have the latest tooling without manual intervention.

## Claude Code cloud sessions

This template ships a `.claude/settings.json` with a `SessionStart` hook that runs `devcontainer up`
in Claude Code cloud sessions (claude.ai/code, `claude --cloud`, routines), so the same devcontainer
your repo already defines gets started automatically instead of leaving Claude on the bare cloud VM.
It's a no-op locally and in Codespaces, where the devcontainer is already the host environment.

To make `pac`, `txc`, `az`, `pwsh`, `terraform`, `func`, and `gh` available to Claude in a cloud
session, add a note like this to the repo's `CLAUDE.md`:

> Power Platform tooling (`pac`, `txc`, `az`, `pwsh`, `terraform`, `func`, `gh`, `dotnet`) runs
> inside the devcontainer, not on the host. Prefix those commands with:
> `devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- <command>`

See [`docs/claude-code-cloud-environment.md`](https://github.com/TALXIS/tools-agentbox/blob/master/docs/claude-code-cloud-environment.md)
in this repo for how to set up the shared cloud environment this hook relies on.
