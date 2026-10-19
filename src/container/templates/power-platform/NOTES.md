# Power Platform Dev Container Template

This template provides a complete, ready-to-use development environment for Power Platform / Dataverse engineers. It works in GitHub Codespaces and in any local devcontainer tool (VS Code Dev Containers, DevPod, the `devcontainer` CLI); no particular editor is required.

## Getting started

The template's `devcontainer.json` is just a name and the pre-built image:

```json
{
  "name": "Power Platform",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest"
}
```

Open the repo in Codespaces or in your devcontainer tool. Add your project's own Features and settings to `devcontainer.json` as usual; they merge with the image's config.

## What the image configures

The image carries its own devcontainer config (a `devcontainer.metadata` label) that your devcontainer
tool merges in, so it updates with the image and your file doesn't change:

- **On every start:** the tools that follow the latest release update, and the keyring starts.
- **On create:** the [TALXIS/skills](https://github.com/TALXIS/skills) plugin is registered with the coding agents present.

The image only covers its own tools. Files to open, forwarded ports, editor settings and extensions,
extra Features and machine size are your project's to set in its `devcontainer.json`.

The tool list is [`devcontainer.features.json`](.devcontainer/devcontainer.features.json).
