# tools-agentbox

Devcontainer features, templates, and pre-built Docker images for Power Platform / Dataverse developers.

**Don't run coding agents directly on your own workstation.** They execute arbitrary commands, which is a
real security exposure, and heavy agent workloads wear on a machine over time. Run them in one of the
isolated environments below instead — see GitHub's own take on why:
[About cloud and local sandboxes](https://docs.github.com/en/copilot/concepts/about-cloud-and-local-sandboxes).

## Configuring the agents

Everything about how the agents behave in an AgentBox — the Skills they get, the instructions loaded
into every session, and the briefing injected at session start — is configured in one place,
[`src/agent/`](src/agent), and applies to both harnesses (Claude Code, GitHub Copilot CLI).

Every environment below already runs the script that applies it, so an edit needs no per-environment
change, and nothing is written into a checkout: the config applies whichever repository is cloned into
the box. See [`src/agent/README.md`](src/agent/README.md) for where each knob lands per harness.

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
  "postCreateCommand": "curl -fsSL --max-time 20 https://talxis.com/agentbox-harness | timeout 90 bash || true"
}
```

**Already have a `devcontainer.json`?** Point `image` at
`ghcr.io/talxis/tools-agentbox/image:latest` and add that same `postCreateCommand` line — it's
easy to end up with a working toolchain but no Skills/MCP registration if this step gets skipped,
since nothing it does is baked into the image itself. It applies [`src/agent/`](src/agent) — the
[TALXIS/skills](https://github.com/TALXIS/skills) plugin (Skills + the `txc` MCP server), the system
prompt, and the initial message — to whichever of Claude Code / GitHub Copilot is present. Omit it if
you don't want that.

Or build your own `devcontainer.json` from individual features listed in
[`devcontainer.features.json`](src/container/templates/power-platform/.devcontainer/devcontainer.features.json).

### 2. GitHub Copilot cloud sandbox

Copilot's cloud agent environment doesn't use `devcontainer.json` — customize it with
[`.github/workflows/copilot-setup-steps.yml`](.github/workflows/copilot-setup-steps.yml) instead.

That step runs as root via `sudo`, while the agent itself runs as the unprivileged runner user, so the
agent config is written to both homes — see
[`src/agent/README.md`](src/agent/README.md#which-home-directories-get-the-user-level-files).

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
directly on the VM, and applies [`src/agent/`](src/agent) — the
[TALXIS/skills](https://github.com/TALXIS/skills) plugin (Skills + the `txc` MCP server), the system
prompt, and the initial message — to Claude Code. No changes needed in individual repos.

To keep `txc` and the Dataverse templates current between setup script runs, merge the `hooks` from
[`src/container/claude-code/session-start-hook.json`](src/container/claude-code/session-start-hook.json) into
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

See [`devcontainer.features.json`](src/container/templates/power-platform/.devcontainer/devcontainer.features.json)
for the current tool/version list.
