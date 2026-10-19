
# Power Apps CLI (pa) (pa-cli)

Installs the Microsoft Power Apps CLI (pa, npm @microsoft/power-apps-cli). Needs Node.js 22 or newer and a Secret Service keyring, which the keyring Feature provides. With version 'latest', add the agentbox Feature to keep it on the latest release.

## Example Usage

```json
"features": {
    "ghcr.io/TALXIS/tools-agentbox/pa-cli:1": {}
}
```

## Options

| Options Id | Description | Type | Default Value |
|-----|-----|-----|-----|
| version | Version of @microsoft/power-apps-cli to install. | string | latest |

## Credential storage

`pa` can only store tokens in the OS credential vault (it hard-codes `usePlaintextFileOnLinux: false`),
so this Feature depends on the [`keyring`](../keyring/NOTES.md) Feature, and `/usr/local/bin/pa` is a
wrapper that starts the keyring before running the real CLI.

## Install location and versions

The npm package installs into `/usr/local/lib/power-apps-cli`, not npm's global prefix, so the
wrapper is the only `pa` on `PATH`. The default `version` is `latest`: `agentbox prerequisite update`
(the agentbox Feature runs it on every start) keeps it current. Set `version` to an exact release
(for example `1.3.0`) to pin it.


---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/features/pa-cli/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
