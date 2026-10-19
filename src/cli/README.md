# agentbox

`agentbox` makes sure the machine a coding agent works on has the AgentBox prerequisites for
Power Platform / Dataverse projects. It reports what's missing or outdated and installs or updates
it. It runs in a container or on a Windows, macOS or Linux workstation, whatever the image. It also
sets up coding-agent harnesses and adds the AgentBox devcontainer config to a repository.

## Run it

It needs the .NET 10 SDK. Run the latest release without installing anything:

```bash
dnx TALXIS.AgentBox.CLI --yes prerequisite validate
```

Or install it as a tool:

```bash
dotnet tool install --global TALXIS.AgentBox.CLI
agentbox prerequisite validate
```

## Commands

| Command | What it does |
|---|---|
| `agentbox prerequisite list` | The prerequisites for this machine, and whether `install` sets each up here or prints the command to run. |
| `agentbox prerequisite validate [ids] [--check-updates] [--deep]` | Checks them: OK, MISSING, OUTDATED or BROKEN. |
| `agentbox prerequisite install [ids] [--reinstall]` | Installs what's missing. It never updates. |
| `agentbox prerequisite update [ids] [--all]` | Brings the prerequisites that follow the latest release up to date, in place, with no restart. `--all` also reinstalls the rest at their latest versions (containers only, slower). |
| `agentbox harness list` | The coding-agent harnesses on this machine, and whether they're set up. |
| `agentbox harness configure [ids]` | Sets them up (registers the [TALXIS/skills](https://github.com/TALXIS/skills) plugin). |
| `agentbox devcontainer create [dir] [--name] [--force-overwrite]` | Writes `.devcontainer/devcontainer.json` for the AgentBox image. |

`prerequisite` has the alias `prereq`, and `devcontainer` has `dc`.

**Bring a running container's tools up to date** (no Codespace restart, no image rebuild):

```bash
dnx TALXIS.AgentBox.CLI --yes prerequisite update
```

## Output and exit codes

Results go to stdout, as text in a terminal and JSON when piped. `-f json|text` forces the format.
Logs go to stderr. `AGENTBOX_LOG_LEVEL` and `AGENTBOX_LOG_FORMAT=json|text` control them.

| Exit code | Meaning |
|---|---|
| 0 | Success |
| 1 | Error. `install`, `update` and `harness configure` return it only with `--strict`, so they never fail a container start. |
| 2 | Invalid input, such as an unknown id or option value |
| 3 | `validate`: a prerequisite is missing or broken |

## Where it installs, and as whom

- **Linux container or cloud VM** (Codespaces, devcontainers, Claude Code cloud, GitHub Actions; or
  set `AGENTBOX_HOST_CLASS=container`):
  - It installs everything, through the AgentBox devcontainer Features pinned in this release.
  - That needs root. Run as another user, agentbox re-runs itself once with `sudo -n` if that works
    without a password; otherwise it says what to run.
- **Workstation:**
  - It installs only user-scope prerequisites.
  - For the rest it prints the command to run, and changes nothing else on the machine.
- **`update` only touches prerequisites AgentBox installed** and that follow the latest release. It
  never touches pinned ones or your own installs.

agentbox keeps its state in `/var/lib/agentbox` (containers, as root) or `~/.agentbox`.

## Privacy

agentbox sends no telemetry. It contacts only nuget.org, the npm registry and ghcr.io (for version
checks and installs), plus whatever the installers it runs download.
