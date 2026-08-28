# Agent configuration

Two manifests configure the agents in an AgentBox, split along the line between the box and the agent:

- **This repo owns the environment.** `agent.json` here declares which plugin marketplaces and
  plugins every box gets, and where the behaviour config lives. Installing and updating tooling is
  also this repo's business.
- **[TALXIS/skills](https://github.com/TALXIS/skills) owns process and know-how.** Its `agent/`
  directory holds how a harness should behave: the instructions loaded into every session, and the
  briefing injected once at session start — colocated with the Skills they belong to.

[`../container/scripts/configure-agent-harness.sh`](../container/scripts/configure-agent-harness.sh)
reads both and applies them. Every surface already runs it — the Claude Code cloud setup script and
the Copilot cloud sandbox via `install-features.sh`, Codespaces via its own `postCreateCommand` — so an
edit on either side reaches all of them with no per-surface change. Re-running is safe and is how an
edit is picked up: JSON is merged, and the markdown files carry a marked `AGENTBOX` block that is
replaced rather than appended, so anything a developer wrote around it survives.

Each manifest exists in exactly one place, and there is no built-in copy to fall back on: a run that
cannot read either one names the URL that failed and exits non-zero rather than leaving a
half-configured box.

## Where each piece lands

Nothing is project-scoped, so the config applies whichever repository is cloned into the box.

| Piece | Claude Code | GitHub Copilot CLI |
|-------|-------------|--------------------|
| Marketplaces / plugins | `claude plugin marketplace add` + `claude plugin install`, with the plugin cache seeded machine-wide via `CLAUDE_CODE_PLUGIN_CACHE_DIR` | `copilot plugin …`, plus `extraKnownMarketplaces`/`enabledPlugins` in `settings.json` |
| Session instructions | `/etc/claude-code/CLAUDE.md` — managed-policy memory: machine-wide, user-independent, and not excludable via a user's `claudeMdExcludes`. Falls back to `~/.claude/CLAUDE.md` when not running as root | `copilot-instructions.md` — user-level custom instructions, loaded in every repository |
| Session briefing | `SessionStart` hook in `settings.json`, returning `hookSpecificOutput.additionalContext` | `sessionStart` hook in `hooks/agentbox.json`, returning `additionalContext` |

Both harnesses get the briefing from one generated script,
`/usr/local/share/agentbox/session-start.sh`, which prints it in whichever JSON shape the calling
harness expects. Don't edit that generated copy.

### Which home directories get the user-level files

Claude's instructions have a machine-level path; the rest are user-level, and the user who runs the
harness is not always the user that provisioned the box. So each user-level file is written to the
invoking user's home, to `/etc/skel` when running as root so a user created later starts configured,
and to the home of the user behind `sudo` when there is one.

That last case is not hypothetical: the Copilot cloud sandbox provisions with
`sudo -E bash install-features.sh`, where `HOME` resolves to `/root`, and then runs the agent as the
unprivileged user. Without the mirror nothing written under `${HOME}` would ever be read there, and
`/etc/skel` doesn't help because that user already exists.

## Short links

| Short link | Resolves to |
|------------|-------------|
| `https://talxis.com/agentbox-agent` | this repo's `src/agent/agent.json` on `master` |
| `https://talxis.com/agentbox-instructions` | `TALXIS/skills` → `agent/instructions.json` on `master` |

Payload files are fetched relative to the URL the instructions manifest actually resolved to, so no
branch, path or filename is pinned here. Point a short link at a branch and that branch's config is
what a box gets. `AGENTBOX_CONFIG_URL` / `AGENTBOX_INSTRUCTIONS_URL` override either at run time, and
`AGENTBOX_CONFIG_DIR` / `AGENTBOX_INSTRUCTIONS_DIR` point at local directories instead (which is how
the tests stay offline).

## Two things to know before writing instructions

- **This is context, not enforcement.** Both harnesses load these files as instructions the model is
  asked to follow, not as constraints on what it can do — and a developer can opt out
  (`copilot --no-custom-instructions`). Anything that must hold regardless belongs in a permission
  rule or a `PreToolUse`/`preToolUse` hook. Each harness does have a machine-level policy channel a
  developer cannot remove (Claude's `/etc/claude-code/managed-settings.json`, Copilot's
  `/etc/github-copilot/policy.d/`); AgentBox deliberately doesn't use them, because it configures
  boxes rather than policing them.
- **Length costs adherence**, and the instructions live in every session's context window. Keep them
  to rules that apply to all work; task-specific guidance belongs in a Skill, which loads on demand.

## Mechanisms deliberately not used

- `COPILOT_CUSTOM_INSTRUCTIONS_DIRS` pointing at a machine-wide directory: it only works where the
  environment is under our control, and as `install-features.sh` notes, neither Claude Code nor a
  GitHub Actions step sources `/etc/profile.d`.
- An `AGENTS.md` in a directory above the clone: Copilot discovers instruction files in the git root
  and the working directory, not above them.

## Verifying

In a session on a freshly provisioned environment:

- Claude Code: `/context` lists the managed `CLAUDE.md` under **Memory files**; `/hooks` shows the
  `SessionStart` entry.
- Copilot CLI: `/instructions` lists `copilot-instructions.md` as an active source.

Or run the assertions, which apply a fixture through every path above without touching the network:

```bash
HOME="$(mktemp -d)" bash test/agent/assert.sh
```
