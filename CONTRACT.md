# AgentBox contract

AgentBox has one job: make sure the computing environment a coding agent runs in has its
prerequisites. That covers a Codespace, a local devcontainer, a Claude Code cloud environment, the
Copilot cloud sandbox, and later a container on Azure Container Instances. It doesn't decide how a
project is set up, which editor or harness people use, or how they sign in to anything.

This page lists what consumers put in their own configuration, and what AgentBox promises about it.
Everything else in this repository is internal and can change at any release.

## Entry points

| Entry point | Who uses it | Consumers write |
|---|---|---|
| Pre-built image | Codespaces, any devcontainer tool | `"image": "ghcr.io/talxis/tools-agentbox/image:latest"` in `devcontainer.json` |
| Setup script | Environments without Docker: Claude Code cloud, Copilot cloud sandbox, plain VMs and containers | `curl -fsSL https://talxis.com/agentbox-setup -o /tmp/agentbox-setup.sh && AGENTBOX_HARNESS=<harness> bash /tmp/agentbox-setup.sh` |
| Harness setup | Older devcontainer configs that call it themselves | `curl -fsSL https://talxis.com/agentbox-harness \| bash` |
| `agentbox` CLI | Anyone, any OS | `dnx TALXIS.AgentBox.CLI --yes <command>` |
| Features | People composing their own `devcontainer.json` | `ghcr.io/talxis/tools-agentbox/<feature>:1` |

The setup script's companion settings, documented in the README (environment variables, managed
settings hook), are part of the contract for as long as the script needs them.

### Inputs and outputs

- **`AGENTBOX_HARNESS`:** `claude`, `copilot` or `none`. It narrows which harness gets set up; left
  unset, every installed one is.
- **`agentbox` commands:** command names, options, exit codes (0 OK, 1 error, 2 bad input,
  3 unhealthy machine) and the fields of `--format json` output.
- **Other `AGENTBOX_*` variables** documented in the [CLI README](src/cli/README.md).

## Promises

- **New capabilities never need a consumer change.** Prerequisites, start/create steps, harnesses,
  commands, options and JSON fields are only ever added, and they reach consumers through the entry
  points above: a new image, a new release behind the same short links, a new CLI version.
- **Nothing above is renamed, removed or changes meaning within a major version.** That covers entry
  points, commands, options, exit codes, JSON fields and variable values. If a break is ever
  unavoidable, it ships as a new major version with the old behavior kept working until consumers
  have moved, announced in the release notes.
- **`image:latest` and the short links follow the latest release.** Versioned image tags
  (`image:<version>`) are there for anyone who wants to pin.
- **AgentBox stays out of the project's and the user's choices:**
  - files to open, ports, editor settings and extensions;
  - extra Features;
  - machine size;
  - sign-in.

## Direction

The `agentbox` CLI becomes the one provisioning engine behind every entry point: the image build,
its start/create hooks, the setup script, and future container hosts. The entry points above stay
as they are while that moves underneath them.
