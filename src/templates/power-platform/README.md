
# Power Platform (power-platform)

Full Power Platform / Dataverse development environment.



# Power Platform Dev Container Template

This template provides a complete, ready-to-use development environment for Power Platform / Dataverse engineers. It works in GitHub Codespaces, VS Code Dev Containers, and can also be used to build a Docker image for Azure Container Instances.

## What's included

See [`devcontainer.features.json`](.devcontainer/devcontainer.features.json) for the current
tool/version list.

## Getting started in Codespaces

1. Click **Use this template** or open the repo in Codespaces
2. Run `pac auth create` or `txc config profile create --url https://your-env.crm4.dynamics.com/` to connect to an environment

## Pre-built image

For faster Codespace startup, use the pre-built image which has all tools baked in:

```json
{
  "image": "ghcr.io/talxis/tools-agentbox/image:latest"
}
```

## Auto-update behavior

`pac` and `txc` auto-update to the latest version on every container start. This ensures workshop participants always have the latest tooling without manual intervention.

## GitHub Copilot Skills

On container creation, this template registers the [TALXIS/skills](https://github.com/TALXIS/skills) plugin with GitHub Copilot — Skills and the `txc` MCP server become available in both Copilot Chat and the `copilot` CLI.


---

_Note: This file was auto-generated from the [devcontainer-template.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/templates/power-platform/devcontainer-template.json).  Add additional notes to a `NOTES.md`._
