# tools-agentbox

Devcontainer features, templates, and pre-built Docker images for Power Platform / Dataverse developers.

**Don't run coding agents directly on your own workstation.** They execute arbitrary commands, which is a
real security exposure, and heavy agent workloads wear on a machine over time. Run them in one of the
isolated environments below instead — see GitHub's own take on why:
[About cloud and local sandboxes](https://docs.github.com/en/copilot/concepts/about-cloud-and-local-sandboxes).

## Choose your environment

### 1. GitHub Codespaces

**New project — create `.devcontainer/devcontainer.json`:**

```json
{
  "name": "Power Platform",
  "image": "ghcr.io/talxis/tools-agentbox/image:latest",
  "customizations": {
    "codespaces": {
      "openFiles": ["README.md"]
    }
  },
  "hostRequirements": {
    "cpus": 2,
    "memory": "8gb"
  },
  "postCreateCommand": "curl -fsSL --max-time 20 https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/scripts/configure-agent-harness.sh | timeout 90 bash || true"
}
```

**Already have a `devcontainer.json`?** Point `image` at
`ghcr.io/talxis/tools-agentbox/image:latest` and add that same `postCreateCommand` line — it's
easy to end up with a working toolchain but no Skills/MCP registration if this step gets skipped,
since nothing it does is baked into the image itself. It registers the
[TALXIS/skills](https://github.com/TALXIS/skills) plugin (Skills + the `txc` MCP server) with
whichever of Claude Code / GitHub Copilot is present — omit it if you don't want that.

Or build your own `devcontainer.json` from individual features listed in
[`devcontainer.features.json`](src/templates/power-platform/.devcontainer/devcontainer.features.json).

### 2. GitHub Copilot cloud sandbox

Copilot's cloud agent environment doesn't use `devcontainer.json` — customize it with
[`.github/workflows/copilot-setup-steps.yml`](.github/workflows/copilot-setup-steps.yml) instead.

### 3. Claude Code cloud environment

Create an org-shared [Claude Code cloud environment](https://code.claude.com/docs/en/cloud-environments)
with:

- **Setup script**:
  ```bash
  curl -fsSL https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/scripts/install-features.sh \
    -o /tmp/agentbox-setup.sh && AGENTBOX_HARNESS=claude bash /tmp/agentbox-setup.sh || echo "agentbox setup failed to download or run" >&2
  ```
- **Environment variables**: `DOTNET_ROOT=/usr/local/dotnet/current`,
  `CLAUDE_CODE_PLUGIN_SEED_DIR=/usr/local/claude-plugin-seed`
- **Network access**: Custom, with the defaults included, plus `cli.github.com`

This installs the Feature list from
[`devcontainer.features.json`](src/templates/power-platform/.devcontainer/devcontainer.features.json)
directly on the VM, and registers the [TALXIS/skills](https://github.com/TALXIS/skills) plugin
(Skills + the `txc` MCP server) with Claude Code. No changes needed in individual repos.

To keep `txc` and the Dataverse templates current between setup script runs, merge the `hooks` from
[`src/claude-code/session-start-hook.json`](src/claude-code/session-start-hook.json) into
[Admin Settings > Claude Code > Managed settings](https://claude.ai/admin-settings/claude-code).

### 4. Docker

```bash
docker pull ghcr.io/talxis/tools-agentbox/image:latest
docker run -it ghcr.io/talxis/tools-agentbox/image:latest bash
```

## Published artifacts

| Artifact | GHCR Reference |
|----------|---------------|
| PAC CLI feature | `ghcr.io/talxis/tools-agentbox/pac-cli:latest` |
| TXC CLI feature | `ghcr.io/talxis/tools-agentbox/txc-cli:latest` |
| Power Platform template | `ghcr.io/talxis/tools-agentbox/power-platform:latest` |
| Pre-built image | `ghcr.io/talxis/tools-agentbox/image:latest` |

## Included tools

See [`devcontainer.features.json`](src/templates/power-platform/.devcontainer/devcontainer.features.json)
for the current tool/version list.
