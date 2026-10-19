# tools-agentbox

Devcontainer features, templates, and pre-built Docker images for Power Platform / Dataverse developers.

**Don't run coding agents directly on your own workstation.** They execute arbitrary commands, which is a
real security exposure, and heavy agent workloads wear on a machine over time. Run them in one of the
isolated environments below instead — see GitHub's own take on why:
[About cloud and local sandboxes](https://docs.github.com/en/copilot/concepts/about-cloud-and-local-sandboxes).

## Choose your environment

### 1. Devcontainers: GitHub Codespaces or any local devcontainer tool

Works in Codespaces (browser, VS Code or JetBrains), VS Code Dev Containers, JetBrains Gateway,
DevPod and the `devcontainer` CLI. No particular editor is required.

**A project only needs this `.devcontainer/devcontainer.json`:**

```json
{
  "name": "Power Platform",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest"
}
```

The image carries its own devcontainer config (a `devcontainer.metadata` label), which every
devcontainer tool merges into yours:
- on every start: update `pac`, `txc`, `pa` and the TALXIS DevKit templates, and start the
  [keyring](src/container/features/keyring/NOTES.md), all in parallel;
- on create: register the [TALXIS/skills](https://github.com/TALXIS/skills) plugin (Skills + the
  `txc` MCP server) with whichever of Claude Code / GitHub Copilot is present;
- `PATH`, forwarded ports for Azure Functions and Azurite, and VS Code extensions for those who use VS Code.

It all updates with the image, so your file never has to change when AgentBox does. The full list is
in [`devcontainer-metadata.json`](src/container/image/power-platform/devcontainer-metadata.json)
plus each Feature's own `postStartCommand`.

**Extending it** (for example a workshop repo): add your own Features and settings on top, as usual.
They merge with the image's config:

```jsonc
{
  "name": "Power Platform workshop",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest",
  "features": { "ghcr.io/devcontainers/features/sshd:1": {} },
  "customizations": {
    "codespaces": { "openFiles": ["README.md"] },
    "vscode": { "settings": { "terminal.integrated.defaultProfile.linux": "pwsh" } }
  },
  "hostRequirements": { "cpus": 4, "memory": "16gb" }
}
```

**Already copied AgentBox config into your `devcontainer.json`?** Keep `image`, and remove the
AgentBox `postStartCommand`/`postCreateCommand` entries (keyring, `pac`/`txc`/`pa`/template updates,
`agentbox-harness`) and the `remoteEnv.PATH` line. They now come from the image; leaving them only
runs them twice.

Or build your own `devcontainer.json` from individual Features listed in
[`devcontainer.features.json`](src/container/templates/power-platform/.devcontainer/devcontainer.features.json).
Each Feature brings its own start-up commands.

### 2. GitHub Copilot cloud sandbox

Copilot's cloud agent environment doesn't use `devcontainer.json` — customize it with
[`.github/workflows/copilot-setup-steps.yml`](.github/workflows/copilot-setup-steps.yml) instead.

### 3. Claude Code cloud environment

Create an org-shared [Claude Code cloud environment](https://code.claude.com/docs/en/cloud-environments)
with:

- **Setup script**:
  ```bash
  curl -fsSL https://talxis.com/agentbox-setup \
    -o /tmp/agentbox-setup.sh && AGENTBOX_HARNESS=claude bash /tmp/agentbox-setup.sh || echo "agentbox setup failed to download or run" >&2
  ```
- **Environment variables**: `DOTNET_ROOT=/usr/local/dotnet/current`,
  `CLAUDE_CODE_PLUGIN_SEED_DIR=/usr/local/claude-plugin-seed`
- **Network access**: Custom, with the defaults included, plus `cli.github.com` and `talxis.com`
  (the setup script's short links redirect through it to raw.githubusercontent.com, which is
  already on the default list)

This installs the Feature list from
[`devcontainer.features.json`](src/container/templates/power-platform/.devcontainer/devcontainer.features.json)
directly on the VM, and registers the [TALXIS/skills](https://github.com/TALXIS/skills) plugin
(Skills + the `txc` MCP server) with Claude Code. No changes needed in individual repos.

To keep `txc` and the Dataverse templates current between setup script runs, merge the `hooks` from
[`src/container/claude-code/session-start-hook.json`](src/container/claude-code/session-start-hook.json) into
[Admin Settings > Claude Code > Managed settings](https://claude.ai/admin-settings/claude-code).

### 4. Docker

```bash
docker pull ghcr.io/talxis/tools-agentbox/image:latest
docker run -it ghcr.io/talxis/tools-agentbox/image:latest bash
```

Plain `docker run` doesn't run devcontainer lifecycle commands, so you get the tool versions the image
was built with and no plugin registration. The keyring still starts on demand, from the shell and the
`pa`/`pac`/`txc` wrappers.

## Published artifacts

| Artifact | GHCR Reference |
|----------|---------------|
| PAC CLI feature | `ghcr.io/talxis/tools-agentbox/pac-cli:latest` |
| TXC CLI feature | `ghcr.io/talxis/tools-agentbox/txc-cli:latest` |
| Power Apps CLI (`pa`) feature | `ghcr.io/talxis/tools-agentbox/pa-cli:latest` |
| Keyring (Secret Service) feature | `ghcr.io/talxis/tools-agentbox/keyring:latest` |
| Power Platform template | `ghcr.io/talxis/tools-agentbox/power-platform:latest` |
| Pre-built image | `ghcr.io/talxis/tools-agentbox/image:latest` |

## Included tools

See [`devcontainer.features.json`](src/container/templates/power-platform/.devcontainer/devcontainer.features.json)
for the current tool/version list. `pa` (Power Apps CLI) follows `latest` and updates on every
container start, like `pac` and `txc`; set the `pa-cli` Feature's `version` option to pin it.

## Credential storage

`txc`, `pac` and `pa` keep sign-in tokens in a gnome-keyring that AgentBox starts and unlocks with an
**empty password**: no better than file permissions, which is fine for ephemeral, single-user
containers. Details: [keyring Feature notes](src/container/features/keyring/NOTES.md).
