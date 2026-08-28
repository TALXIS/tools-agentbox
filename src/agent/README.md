# Agent configuration

The one place agent behaviour is configured for **every** AgentBox surface (Claude Code cloud
environment, Copilot cloud sandbox, Codespaces, Docker) and **every** harness (Claude Code, GitHub
Copilot CLI). Three knobs live in this directory:

- the **Skills list** — which plugin marketplaces and plugins get registered,
- the **system prompt** — instructions loaded into every session,
- the **initial message** — context injected once per session, at session start.

[`../container/scripts/configure-agent-harness.sh`](../container/scripts/configure-agent-harness.sh)
applies all three. Every surface already runs it — the Claude Code cloud setup script and the Copilot
cloud sandbox via `install-features.sh`, Codespaces via its own `postCreateCommand` — so an edit here
reaches all of them with no per-surface change. Re-running is safe and is how an edit is picked up:
JSON is merged, and the markdown files carry a marked `AGENTBOX` block that is replaced rather than
appended, so anything a developer wrote around it survives.

The config exists here and nowhere else. There is no built-in copy in the script to fall back on: a
run that cannot read this directory (or the short link below) fails instead of leaving a
half-configured box.

## Where each knob lands

Nothing is project-scoped, so the config applies whichever repository is cloned into the box.

| Knob | Claude Code | GitHub Copilot CLI |
|------|-------------|--------------------|
| Skills / plugins | `claude plugin marketplace add` + `claude plugin install`, with the plugin cache seeded machine-wide via `CLAUDE_CODE_PLUGIN_CACHE_DIR` | `copilot plugin …`, plus `extraKnownMarketplaces`/`enabledPlugins` in `settings.json` |
| System prompt | `/etc/claude-code/CLAUDE.md` — managed-policy memory: machine-wide, user-independent, and not excludable via a user's `claudeMdExcludes`. Falls back to `~/.claude/CLAUDE.md` when not running as root | `copilot-instructions.md` — user-level custom instructions, loaded in every repository |
| Initial message | `SessionStart` hook in `settings.json`, returning `hookSpecificOutput.additionalContext` | `sessionStart` hook in `hooks/agentbox.json`, returning `additionalContext` |

Both harnesses get the initial message from one generated script,
`/usr/local/share/agentbox/session-start.sh`, which prints the message in whichever JSON shape the
calling harness expects. Don't edit that generated copy.

### Which home directories get the user-level files

Claude's system prompt has a machine-level path; the rest are user-level, and the user who runs the
harness is not always the user that provisioned the box. So each user-level file is written to:

1. the invoking user's home,
2. `/etc/skel`, when running as root, so a user created later starts configured,
3. the home of the user behind `sudo`, when there is one.

That third case is not hypothetical: the Copilot cloud sandbox provisions with
`sudo -E bash install-features.sh`, where `HOME` resolves to `/root`, and then runs the agent as the
unprivileged user. Without the mirror, nothing written under `${HOME}` would ever be read there, and
`/etc/skel` doesn't help because that user already exists.

## Two things to know before writing instructions

- **This is context, not enforcement.** Both harnesses load these files as instructions the model is
  asked to follow, not as constraints on what it can do — and a developer can opt out
  (`copilot --no-custom-instructions`). Anything that must hold regardless of what the model decides
  belongs in a permission rule or a `PreToolUse`/`preToolUse` hook instead. Each harness does have a
  machine-level policy channel that a developer cannot remove (Claude's
  `/etc/claude-code/managed-settings.json`, Copilot's `/etc/github-copilot/policy.d/`); AgentBox
  deliberately doesn't use them, because it configures boxes rather than policing them.
- **Length costs adherence.** The system prompt is in the context window of every session, every
  time. Keep it to rules that apply to all work in an AgentBox; anything task-specific belongs in a
  Skill in [TALXIS/skills](https://github.com/TALXIS/skills), which loads on demand.

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

Or run the assertions, which apply the config and check every path above:

```bash
HOME="$(mktemp -d)" bash test/agent/assert.sh
```

## How the config is found

`configure-agent-harness.sh` resolves this directory from `AGENTBOX_CONFIG_DIR` if set (used by the
tests), else a sibling of the script when it runs from a checkout, else over the network from
`AGENTBOX_CONFIG_URL` — default `https://talxis.com/agentbox-agent`, which redirects to this
directory's manifest.

One short link covers it: the payload files are fetched relative to the URL the manifest actually
resolved to, so they follow its branch and path automatically. Point the short link at a branch and
that branch's prompt and message are what a box gets.
