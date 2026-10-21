# Playwright CLI (playwright-cli) (playwright-cli)

Installs Playwright's CLI for coding agents (playwright-cli, from npm playwright) with a browser it can launch, or reuses a Playwright Chromium that's already installed. Pinned: each Playwright release launches only its own browser build. Needs Node.js 22 or newer.

## Example Usage

```json
"features": {
    "ghcr.io/TALXIS/tools-agentbox/playwright-cli:1": {}
}
```

## Options

| Options Id | Description | Type | Default Value |
|-----|-----|-----|-----|
| version | Version of the playwright npm package. Match Microsoft.Playwright in your .NET tests so they find the same browser. | string | 1.63.0 |

## Browser

Each Playwright release launches only its own browser build. The Feature installs, in order of
preference:

1. Nothing, when the build for this version is already in `PLAYWRIGHT_BROWSERS_PATH`.
2. Nothing, when another Playwright Chromium is there (Claude Code cloud ships one) and
   `playwright-cli` launches it. The wrapper points the CLI at it. If it doesn't launch, the Feature
   fails and removes what it installed.
3. Chromium, its headless shell and the system libraries, into `PLAYWRIGHT_BROWSERS_PATH` (default
   `/usr/local/share/pw-browsers`). Every user can launch them and add the build another Playwright
   version needs (for example `pwsh bin/Debug/<tfm>/playwright.ps1 install chromium` in a .NET test
   project); the sticky bit keeps users from deleting builds they didn't install.
   `PLAYWRIGHT_SKIP_BROWSER_GC=1` stops one install from removing another's browser.

## Versions

The `version` is pinned: `agentbox prerequisite update` never changes it, because a new Playwright
needs a new browser. Keep it equal to `Microsoft.Playwright` in your .NET tests, and set
`PLAYWRIGHT_BROWSERS_PATH` for them, so they launch the same browser without downloading one.

## Wrapper

`/usr/local/bin/playwright-cli` runs `playwright cli` from `/usr/local/lib/playwright-cli`. Unless
already set, it selects Playwright's Chromium (`PLAYWRIGHT_MCP_BROWSER=chromium`; the CLI's default
is the system Google Chrome), the browsers folder, and `USER`, which the CLI's sockets depend on.


---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/features/playwright-cli/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
