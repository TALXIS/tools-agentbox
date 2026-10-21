## Browser

Each Playwright release launches only its own browser build. The Feature installs, in order of
preference:

1. Only what's missing of the headless shell and ffmpeg, when the build for this version is already
   in `PLAYWRIGHT_BROWSERS_PATH`.
2. Nothing, when another Playwright Chromium is there (Claude Code cloud ships one) and
   `playwright-cli` launches it. The wrapper points the CLI at it.
3. Chromium, its headless shell and the system libraries, into `PLAYWRIGHT_BROWSERS_PATH` (default
   `/usr/local/share/pw-browsers`). Every user can launch them and add the build another Playwright
   version needs (for example `pwsh bin/Debug/<tfm>/playwright.ps1 install chromium` in a .NET test
   project); the sticky bit keeps users from deleting builds they didn't install.
   `PLAYWRIGHT_SKIP_BROWSER_GC=1` stops one install from removing another's browser.

Whenever the browser doesn't launch, the Feature fails and puts back the `playwright-cli` and install
folder that were there before.

## Versions

The `version` is pinned: `agentbox prerequisite update` never changes it, because a new Playwright
needs a new browser. Keep it equal to `Microsoft.Playwright` in your .NET tests, and set
`PLAYWRIGHT_BROWSERS_PATH` for them, so they launch the same browser without downloading one.

## Wrapper

`/usr/local/bin/playwright-cli` runs `playwright cli` from `/usr/local/lib/playwright-cli`. Unless
already set, it selects Playwright's Chromium (`PLAYWRIGHT_MCP_BROWSER=chromium`; the CLI's default
is the system Google Chrome), the browsers folder, and `USER`, which the CLI's sockets depend on.
