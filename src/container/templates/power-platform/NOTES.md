# Power Platform Dev Container Template

This template provides a complete, ready-to-use development environment for Power Platform / Dataverse engineers. It works in GitHub Codespaces and in any local devcontainer tool (VS Code Dev Containers, JetBrains Gateway, DevPod, the `devcontainer` CLI); no particular editor is required.

## What's included

See [`devcontainer.features.json`](.devcontainer/devcontainer.features.json) for the current
tool/version list.

## Getting started

The template's `devcontainer.json` is just a name and the pre-built image:

```json
{
  "name": "Power Platform",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest"
}
```

1. Open the repo in Codespaces or in your devcontainer tool.
2. Run `pac auth create` or `txc config profile create --url https://your-env.crm4.dynamics.com/` to connect to an environment.

Add your project's own Features and settings to `devcontainer.json` as usual; they merge with the image's config.

## What the image configures

The image carries its own devcontainer config (a `devcontainer.metadata` label) that your devcontainer
tool merges in, so it updates with the image and your file doesn't change:

- **On every start:** `pac`, `txc`, `pa` and the TALXIS DevKit templates update to the latest version, and the keyring starts. These run in parallel.
- **On create:** the [TALXIS/skills](https://github.com/TALXIS/skills) plugin (Skills + the `txc` MCP server) is registered with whichever of Claude Code or GitHub Copilot is present. For Copilot, that means both Copilot Chat and the `copilot` CLI.
- `PATH`, forwarded ports for Azure Functions and Azurite, and VS Code extensions (ignored by other editors).

## Credential storage

`txc`, `pac` and `pa` keep sign-in tokens in a gnome-keyring unlocked with an empty password; see the [keyring Feature notes](../../features/keyring/NOTES.md).
