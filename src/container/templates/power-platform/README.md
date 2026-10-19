
# Power Platform (power-platform)

Full Power Platform / Dataverse development environment. A minimal devcontainer.json: the pre-built image brings its own tool updates, keyring and agent plugin setup, so the file needs no changes when AgentBox does.



# Power Platform Dev Container Template

This template provides a complete, ready-to-use development environment for Power Platform / Dataverse engineers. It works in GitHub Codespaces and in any local devcontainer tool (VS Code Dev Containers, JetBrains Gateway, DevPod, the `devcontainer` CLI); no particular editor is required.

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
- Forwarded ports, and VS Code extensions (ignored by other editors).

The tool list is [`devcontainer.features.json`](.devcontainer/devcontainer.features.json).

---

_Note: This file was auto-generated from the [devcontainer-template.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/templates/power-platform/devcontainer-template.json).  Add additional notes to a `NOTES.md`._
