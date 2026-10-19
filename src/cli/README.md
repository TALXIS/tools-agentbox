# agentbox

One command to make any machine or container ready for coding agents working on Power Platform /
Dataverse: the tools they need, kept current, and the agent harnesses set up with the
[TALXIS/skills](https://github.com/TALXIS/skills) plugin.

```bash
dnx TALXIS.AgentBox.CLI --yes prerequisite validate     # what's missing or outdated
dnx TALXIS.AgentBox.CLI --yes prerequisite update       # bring it up to date, in place
```

`dnx` comes with the .NET 10 SDK and always runs the latest release. `agentbox --help` describes
every command.

**In a container or cloud VM** it installs the full AgentBox tool set from the same pinned
devcontainer Features the pre-built image uses (as root, through `sudo` if needed), and keeps it
current. **On your own machine** it only checks: it shows what's missing and the command to install
it, and sets up the agent harnesses. Agents are better off in a sandbox anyway.

| Exit code | Meaning |
|---|---|
| 0 | Done |
| 1 | Something failed |
| 2 | Invalid input |
| 3 | `validate`: not ready (something missing or unhealthy) |

Results go to stdout (text in a terminal, JSON when piped), logs to stderr. No telemetry.
