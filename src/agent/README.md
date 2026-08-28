# Agent configuration

The one place agent behaviour is configured for **every** AgentBox surface (Claude Code cloud
environment, Copilot cloud sandbox, Codespaces, Docker) and **every** harness (Claude Code, GitHub
Copilot CLI). Edit these files and nothing else:

| File | What it controls |
|------|------------------|
| [`agent.json`](agent.json) | The Skills list — which plugin marketplaces and plugins get registered |
| [`system-prompt.md`](system-prompt.md) | Instructions loaded into **every** session |
| [`initial-message.md`](initial-message.md) | Context injected **once per session**, at session start |

[`../container/scripts/configure-agent-harness.sh`](../container/scripts/configure-agent-harness.sh)
applies all three. Every surface already runs it — the Claude Code cloud setup script and the Copilot
cloud sandbox via `install-features.sh`, Codespaces via its own `postCreateCommand` — so an edit here
reaches all of them with no per-surface change. Re-running is safe and is how an edit is picked up:
JSON is merged, and the markdown files carry a marked `AGENTBOX` block that is replaced rather than
appended, so anything a developer wrote around it survives.

## Where each knob lands

| Knob | Claude Code | GitHub Copilot CLI |
|------|-------------|--------------------|
| Skills / plugins | `claude plugin marketplace add` + `claude plugin install` | `copilot plugin ...`, plus `extraKnownMarketplaces`/`enabledPlugins` in `~/.copilot/settings.json` (and `/etc/skel` for users created later) |
| System prompt | `/etc/claude-code/CLAUDE.md` — managed-policy memory: machine-wide, user-independent, and not excludable via a user's `claudeMdExcludes`. Falls back to `~/.claude/CLAUDE.md` when not running as root | `~/.copilot/copilot-instructions.md` — user-level custom instructions, loaded in every repository (and `/etc/skel`) |
| Initial message | `SessionStart` hook merged into `~/.claude/settings.json`, returning `hookSpecificOutput.additionalContext` | `sessionStart` hook in `~/.copilot/hooks/agentbox.json`, returning `additionalContext` |

Both harnesses get the initial message from one generated script,
`/usr/local/share/agentbox/session-start.sh`, which prints `initial-message.md` in whichever JSON
shape the calling harness expects. Don't edit that generated copy.

## Two things to know before writing instructions

- **This is context, not enforcement.** Both harnesses load these files as instructions the model is
  asked to follow, not as constraints on what it can do — and a developer can opt out
  (`copilot --no-custom-instructions`). Anything that must hold regardless of what the model decides
  belongs in a permission rule or a `PreToolUse`/`preToolUse` hook instead.
- **Length costs adherence.** Everything in `system-prompt.md` is in the context window of every
  session, every time. Keep it to rules that apply to all work in an AgentBox; anything task-specific
  belongs in a Skill in [TALXIS/skills](https://github.com/TALXIS/skills), which loads on demand.

## Verifying

In a session on a freshly provisioned environment:

- Claude Code: `/context` lists `/etc/claude-code/CLAUDE.md` (or `~/.claude/CLAUDE.md`) under
  **Memory files**; `/hooks` shows the `SessionStart` entry.
- Copilot CLI: `/instructions` lists `copilot-instructions.md` as an active source.

Or run the assertions against a live box, which apply the config and check every path above:

```bash
HOME="$(mktemp -d)" bash test/agent/assert.sh
```

## How the config is found

`configure-agent-harness.sh` resolves this directory in order:

1. `AGENTBOX_CONFIG_DIR`, if set (used by the tests).
2. A `src/agent/` sibling, when the script runs from a repository checkout.
3. Over the network: `AGENTBOX_CONFIG_URL` (default `https://talxis.com/agentbox-agent`), then the
   `raw.githubusercontent.com` URL for `master` as a fallback. `system-prompt.md` and
   `initial-message.md` are fetched relative to wherever the manifest resolved from, so pointing the
   short link at `src/agent/agent.json` on a branch picks up that branch's payload files too.

If the config can't be reached at all, the script falls back to registering
`implement@talxis` from `TALXIS/skills` — the behaviour it had before it was config-driven — so an
offline run never silently leaves a box with no Skills.
