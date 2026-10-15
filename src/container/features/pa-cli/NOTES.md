## Credential storage

`pa` can only store tokens in the OS credential vault (it hard-codes `usePlaintextFileOnLinux: false`),
so this Feature depends on the [`keyring`](../keyring/NOTES.md) Feature, and `/usr/local/bin/pa` is a
wrapper that starts the keyring before running the real CLI.

## Install location and versions

The npm package installs into `/usr/local/lib/power-apps-cli`, not npm's global prefix, so the
wrapper is the only `pa` on `PATH`. The default `version` is `latest`, like the other CLIs in the
image: the Feature then updates `pa` on every container start. Set `version` to an exact release
(for example `1.3.0`) to pin it; pinning turns off the update.
