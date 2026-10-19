# tools-agentbox

Devcontainer features, templates, and pre-built Docker images for Power Platform / Dataverse developers.

**Don't run coding agents directly on your own workstation.** They execute arbitrary commands, which is a
real security exposure, and heavy agent workloads wear on a machine over time. Run them in one of the
isolated environments below instead — see GitHub's own take on why:
[About cloud and local sandboxes](https://docs.github.com/en/copilot/concepts/about-cloud-and-local-sandboxes).

## Choose your environment

### 1. Devcontainers: GitHub Codespaces or any local devcontainer tool

Works in Codespaces, VS Code Dev Containers, DevPod and the `devcontainer` CLI: any tool that
follows the devcontainer spec. No particular editor is required.

**A project only needs this `.devcontainer/devcontainer.json`:**

```json
{
  "name": "Power Platform",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest"
}
```

The image carries its own devcontainer config (a `devcontainer.metadata` label), which every
devcontainer tool merges into yours:
- on every start: update the tools that follow the latest release, and start the keyring;
- on create: register the [TALXIS/skills](https://github.com/TALXIS/skills) plugin with the coding
  agents present.

The image only covers its own tools. Everything about the project and the editor is the
repository's to set in its `devcontainer.json`: files to open, forwarded ports, editor settings and
extensions, extra Features, machine size.

It all updates with the image, so your file never has to change when AgentBox does. The full list is
in [`devcontainer-metadata.json`](src/container/image/power-platform/devcontainer-metadata.json)
plus each Feature's own `postStartCommand`.

**Adding the project's own config** (for example a workshop repo): add it on top, as usual. It merges
with the image's config:

```jsonc
{
  "name": "Power Platform workshop",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest",
  "features": { "ghcr.io/devcontainers/features/sshd:1": {} },
  "forwardPorts": [7071],
  "customizations": {
    "codespaces": { "openFiles": ["README.md"] },
    "vscode": { "settings": { "editor.formatOnSave": true } }
  }
}
```

**Already copied AgentBox config into your `devcontainer.json`?** Keep `image`, and remove the
AgentBox `postStartCommand`/`postCreateCommand` entries and the `remoteEnv.PATH` line. The image
brings the commands (leaving them only runs them twice), and its tools are on `PATH` without it.

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
with Claude Code. No changes needed in individual repos.

To keep the tools current between setup script runs, merge the `hooks` from
[`src/container/claude-code/session-start-hook.json`](src/container/claude-code/session-start-hook.json) into
[Admin Settings > Claude Code > Managed settings](https://claude.ai/admin-settings/claude-code).

### 4. Docker

```bash
docker pull ghcr.io/talxis/tools-agentbox/image:latest
docker run -it ghcr.io/talxis/tools-agentbox/image:latest bash
```

Plain `docker run` doesn't run devcontainer lifecycle commands, so you get the tool versions the image
was built with and no plugin registration. The keyring still starts on demand, from the shell and the
wrappers in `/usr/local/bin`.

## Published artifacts

- Every Feature under [`src/container/features/`](src/container/features) as
  `ghcr.io/talxis/tools-agentbox/<feature>`
- The devcontainer template: `ghcr.io/talxis/tools-agentbox/power-platform`
- The pre-built image: `ghcr.io/talxis/tools-agentbox/image:latest`

## Included tools

See [`devcontainer.features.json`](src/container/templates/power-platform/.devcontainer/devcontainer.features.json).
