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
