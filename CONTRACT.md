# AgentBox contract

AgentBox makes sure the environment a coding agent runs in has its prerequisites. It doesn't decide
how a project is set up, which editor or agent harness people use, or how they sign in to anything.

These are the things consumers put in their own configuration. Everything else in this repository
is internal and can change at any release.

| Entry point | For | Consumers write |
|---|---|---|
| Pre-built image | Codespaces, any devcontainer tool | `"image": "ghcr.io/talxis/tools-agentbox/image:latest"` |
| Features | Their own devcontainer composition | `ghcr.io/talxis/tools-agentbox/<feature>:1` |
| Setup script | Machines dedicated to agents: Claude Code cloud, the Copilot cloud agent, VMs, containers | `curl -fsSL https://talxis.com/agentbox-setup \| bash` |
| `agentbox` CLI | Anyone, any OS | `dnx TALXIS.AgentBox.CLI --yes <command>`, or `agentbox` where the image or Feature installed it |

The CLI's commands, options, exit codes and `--format json` fields are part of it.
`https://talxis.com/agentbox-harness` keeps working for configs that still call it.

## Promises

- **New capabilities never need a consumer change.** Tools, harnesses, commands, options and JSON
  fields are only added, and they arrive through the entry points above.
- **Nothing above is renamed, removed or changes meaning within a major version.** An unavoidable
  break ships as a new major version, announced in the release notes, with the old one kept working
  until consumers have moved.
- **`image:latest` and the short links follow the latest release.** `image:<version>` is there to
  pin.
- **Tools that follow their latest release stay current without a rebuild:** on every container
  start, and on demand with `agentbox prerequisite update`.
- **AgentBox stays out of the project's and the user's choices:** files to open, ports, editor
  settings and extensions, extra Features, machine size, sign-in.
