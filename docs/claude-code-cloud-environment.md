# Claude Code cloud environment for the agentbox toolchain

This sets up an organization-shared [Claude Code cloud
environment](https://code.claude.com/docs/en/cloud-environments) so that Claude Code cloud
sessions (claude.ai/code, `claude --cloud`, routines, Claude Tag, etc.) get the same Power
Platform / Dataverse toolchain as the `tools-agentbox` devcontainer — without maintaining a
second definition of that toolchain anywhere.

## Why this isn't just "point the environment at our image"

Claude Code's Anthropic-hosted cloud environments only configure a name, a network access level,
environment variables, and a setup script (a Bash script that runs once on a stock Ubuntu 24.04 VM
before Claude Code launches). There's no field to reference a custom Docker image — the docs are
explicit that replacing the base image entirely isn't supported yet.

What the VM *does* have is Docker and Node.js preinstalled, which is enough to run the same
[`devcontainer` CLI](https://github.com/devcontainers/cli) that GitHub Codespaces and VS Code Dev
Containers use. Pointed at a repo's existing `.devcontainer/devcontainer.json`, it builds/pulls the
same `ghcr.io/talxis/tools-agentbox/image:latest` and runs the same lifecycle commands as
Codespaces — so the devcontainer definition stays the one place it's maintained
(`src/images/power-platform/Dockerfile` and `src/templates/power-platform/.devcontainer/`), and
nothing gets reimplemented as a parallel setup script.

If your team instead needs Claude Code itself — not just the tooling — to execute inside your own
network, that's a [self-hosted environment](https://code.claude.com/docs/en/self-hosted-environments),
a bigger infrastructure commitment (you run your own runner fleet) that's out of scope here.

## How it fits together

- **The cloud environment's setup script** runs once (cached ~7 days) and only warms the cache: it
  installs `@devcontainers/cli` and pre-pulls the image, so it's already on disk for every session
  after the first. It never starts the container — the docs note the environment cache keeps files
  on disk, not running processes, so anything started here wouldn't still be running once a later
  session resumes from the cache.
- **A `SessionStart` hook**, shipped in this repo's `power-platform` template at
  `src/templates/power-platform/.claude/settings.json`, runs on every session start (cloud sessions
  only, and only when the repo has a `.devcontainer/`) and actually starts the container:
  `devcontainer up --workspace-folder "$CLAUDE_PROJECT_DIR"`. Any repo generated from this template
  gets the hook automatically, alongside the devcontainer.json it activates.
- Claude then runs Power-Platform tooling (`pac`, `txc`, `az`, `pwsh`, `terraform`, `func`, `gh`,
  `dotnet`) inside that container via `devcontainer exec`, since that's where the tools actually
  live, not on the bare cloud VM. Add a note to the consuming repo's `CLAUDE.md`, e.g.:

  > Power Platform tooling (`pac`, `txc`, `az`, `pwsh`, `terraform`, `func`, `gh`, `dotnet`) runs
  > inside the devcontainer, not on the host. Prefix those commands with:
  > `devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- <command>`

This changes nothing about the Dockerfile, the published image, or Codespaces/local VS Code — they
keep working exactly as they do today. It's the same freshness trade-off Codespaces already has:
`pac`/`txc` self-update on every container start (see
`src/features/pac-cli/devcontainer-feature.json`'s `postStartCommand`), and everything else in the
image refreshes on the existing weekly rebuild in `build-image.yaml`.

## Set it up

1. Go to **[claude.ai/admin-settings/cloud-environments](https://claude.ai/admin-settings/cloud-environments)**.
2. Under **Anthropic-hosted environments**, select **New**.
3. Name it (e.g. "Power Platform / Agentbox").
4. Leave **Network access** at **Trusted** — `ghcr.io` (container registries) and
   `registry.npmjs.org` are both already on the default Trusted allowlist, so no custom domains are
   needed.
5. Paste the contents of
   [`setup-script.sh`](claude-code-cloud-environment/setup-script.sh) into **Setup script**.
6. Create the environment. Optionally set it as your organization's default at
   [claude.ai/admin-settings/claude-code](https://claude.ai/admin-settings/claude-code), or leave it
   opt-in per session/routine.

Any repo checked out into a cloud session that was built from the `power-platform` template (i.e.
has `.devcontainer/` and the bundled `.claude/settings.json`) will now start its devcontainer
automatically at session start.

## Verify it

Start a cloud session in this environment against a repo using the template, and ask Claude to run:

```bash
devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- pac help
devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- txc --version
devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- az --version
devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- pwsh -v
devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- terraform -v
devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- func --version
devcontainer exec --workspace-folder "$CLAUDE_PROJECT_DIR" -- gh --version
```
