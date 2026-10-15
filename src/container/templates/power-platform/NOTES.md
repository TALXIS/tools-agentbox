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

`pac`, `txc` and `pa` auto-update to the latest version on every container start. This ensures workshop participants always have the latest tooling without manual intervention.

## Credential storage

Every container start also starts a D-Bus session bus and an unlocked gnome-keyring Secret Service (`agentbox-keyring-init`), so `txc`, `pac` and `pa` store sign-in tokens in the OS credential vault. The keyring has an empty password, so it protects secrets with file permissions only — fine for ephemeral, single-user containers. See the [secret-service Feature notes](../../features/secret-service/NOTES.md).

## Agent harness setup

On container creation, this template registers the [TALXIS/skills](https://github.com/TALXIS/skills) plugin (Skills + the `txc` MCP server) with whichever of Claude Code or GitHub Copilot is present — for Copilot, that means both Copilot Chat and the `copilot` CLI.
