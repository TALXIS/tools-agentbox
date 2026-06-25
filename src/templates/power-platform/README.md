
# Power Platform (power-platform)

Full Power Platform / Dataverse development environment. Includes .NET 10 SDK, Node.js 22, Azure CLI, PowerShell, Terraform, GitHub CLI, GitHub Copilot CLI, Power Platform CLI (pac), TALXIS DevKit CLI (txc), and Azure Functions Core Tools.



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


---

_Note: This file was auto-generated from the [devcontainer-template.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/templates/power-platform/devcontainer-template.json).  Add additional notes to a `NOTES.md`._
