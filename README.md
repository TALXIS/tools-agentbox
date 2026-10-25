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
- **Network access:** Custom, with the defaults, plus `cli.github.com` and `talxis.com`. With a
  stricter list, setup also needs `ghcr.io`, `pkg-containers.githubusercontent.com`, `api.nuget.org`,
  `registry.npmjs.org`, `packages.microsoft.com`, `releases.hashicorp.com`, and `dot.net` with
  `builds.dotnet.microsoft.com` when the machine lacks .NET 10.
- The setup script fails when a tool didn't make it, so a broken environment shows in its log.
- **Optional:** to update the tools as sessions start, merge the hook in
  [`session-start-hook.json`](src/container/claude-code/session-start-hook.json) into
  [managed settings](https://claude.ai/admin-settings/claude-code).

## Keeping current

Tools that follow their latest release, and the TALXIS/skills plugin in the agent harnesses, update
on every container start. Mid-session:

```bash
agentbox prerequisite update   # the tools
agentbox harness update        # the plugin (a new agent session picks it up)
```

The [`agentbox` CLI](src/cli/README.md) also checks a machine (`prerequisite validate`,
`harness validate`). The [contract](CONTRACT.md) lists what stays stable. The tools are listed in
[`agentbox-features.json`](src/container/agentbox-features.json).
