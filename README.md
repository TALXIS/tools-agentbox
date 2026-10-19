# tools-agentbox

A ready environment for coding agents doing Power Platform and Dataverse work: the tools they need,
kept current, and Claude Code and GitHub Copilot set up with the [TALXIS/skills](https://github.com/TALXIS/skills)
plugin. Wherever the agent runs, it gets the same tools.

**Don't run coding agents directly on your own workstation.** They run arbitrary commands. Give them
a sandbox: [About cloud and local sandboxes](https://docs.github.com/en/copilot/concepts/about-cloud-and-local-sandboxes).

## Pick your sandbox

**Codespaces or any devcontainer tool.** Your `.devcontainer/devcontainer.json` needs only the image:

```json
{ "name": "Power Platform", "image": "ghcr.io/talxis/tools-agentbox/image:latest" }
```

Composing your own instead? Each tool is a [Feature](src/container/features); add `agentbox` to keep
them current. On Windows, use *Dev Containers: Clone Repository in Container Volume* rather than
opening a Windows folder: Linux line endings and file modes, and much faster.

**GitHub Copilot cloud agent.** Add a step to your `.github/workflows/copilot-setup-steps.yml`:

```yaml
      - name: Set up AgentBox
        run: curl -fsSL https://talxis.com/agentbox-setup | bash
```

**Claude Code cloud environment.** In an org-shared [cloud environment](https://code.claude.com/docs/en/cloud-environments):

- **Setup script:** `curl -fsSL https://talxis.com/agentbox-setup | bash`
- **Environment variable:** `CLAUDE_CODE_PLUGIN_SEED_DIR=/usr/local/claude-plugin-seed`
- **Network access:** Custom, with the defaults, plus `cli.github.com` and `talxis.com`
- **Optional:** to update the tools as sessions start, merge the hook in
  [`session-start-hook.json`](src/container/claude-code/session-start-hook.json) into
  [managed settings](https://claude.ai/admin-settings/claude-code).

## Keeping current

Tools that follow their latest release update on every container start. Mid-session, or anywhere
else:

```bash
agentbox prerequisite update
```

The [`agentbox` CLI](src/cli/README.md) also checks a machine (`prerequisite validate`,
`harness validate`). The [contract](CONTRACT.md) lists what stays stable. The tools are listed in
[`devcontainer.features.json`](src/container/templates/power-platform/.devcontainer/devcontainer.features.json).
