
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

Each Playwright release launches only its own browser build. The Feature uses, in this order:

1. that build, if it's already in `PLAYWRIGHT_BROWSERS_PATH` (adding its headless shell if missing);
2. another Playwright Chromium already there, if `playwright-cli` launches it (Claude Code cloud
   ships one);
3. that build, downloaded with its system libraries into `PLAYWRIGHT_BROWSERS_PATH` (default
   `/usr/local/share/pw-browsers`).

If the browser doesn't launch, the Feature fails and leaves what was installed before in place.

## Version

`version` is pinned; `agentbox prerequisite update` never changes it. Keep it equal to
`Microsoft.Playwright` in your .NET tests and set `PLAYWRIGHT_BROWSERS_PATH` for them, and they
launch the same browser without downloading one.

## Shared folder

Every user can launch the browsers and add the build their own Playwright version needs (for
example `playwright.ps1 install chromium` in a .NET test project). The sticky bit keeps them from
deleting builds they didn't install, and `PLAYWRIGHT_SKIP_BROWSER_GC=1` stops one install from
removing another's.


---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/features/playwright-cli/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
