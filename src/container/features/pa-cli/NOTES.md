## Credential storage

`pa` stores tokens only in the OS credential vault: it hard-codes `usePlaintextFileOnLinux: false`,
with no flag or environment variable to change it. Without a running, unlocked Secret Service every
command fails with `GnomeKeyringError: Couldn't access platform storage: AccessDenied`. This Feature
therefore depends on the [`secret-service`](../secret-service) Feature, and `/usr/local/bin/pa` is a
wrapper that starts the keyring (`agentbox-keyring-init`) before running the real CLI. See that
Feature's notes for the empty-password trade-off.

## Install location and versions

The npm package installs into `/usr/local/lib/power-apps-cli`, not npm's global prefix, so the
wrapper is the only `pa` on `PATH`. The default `version` is `latest`, like the other CLIs in the
image: the Feature then updates `pa` on every container start. Set `version` to an exact release
(for example `1.3.0`) to pin it; pinning turns off the update.
