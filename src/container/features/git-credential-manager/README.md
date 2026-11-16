
# Git Credential Manager (git-credential-manager)

Installs Git Credential Manager (GCM) as a .NET tool and configures it as Git's credential helper for every user, storing credentials in the Secret Service keyring, which the keyring Feature provides. With version 'latest', add the agentbox Feature to keep it on the latest release.

## Example Usage

```json
"features": {
    "ghcr.io/TALXIS/tools-agentbox/git-credential-manager:1": {}
}
```

## Options

| Options Id | Description | Type | Default Value |
|-----|-----|-----|-----|
| version | Version of git-credential-manager to install. | string | latest |

## How it works

The Feature does what `dotnet tool install -g git-credential-manager && git-credential-manager configure`
does on a workstation, adjusted for a container shared by several users:

- The tool goes into `/usr/local/share/git-credential-manager` (`--tool-path`), not root's
  `~/.dotnet/tools`, so every user can run it.
- `/usr/local/bin/git-credential-manager` is a wrapper that sets `DOTNET_ROOT` (and
  `DOTNET_ROLL_FORWARD=Major`, so it runs where only a newer .NET major is installed) and starts
  the keyring with `agentbox-keyring-init` before running GCM. Git runs credential helpers from
  whatever process calls it (agents, CI steps, `docker run`), which often has neither.
- `git-credential-manager configure --system` writes the helper into `/etc/gitconfig`, so it applies
  to every user. The helper path is then switched to the wrapper, and
  `credential.credentialStore` is set to `secretservice`.

## Security trade-off

Credentials are stored in the keyring Feature's gnome-keyring, which is unlocked with an empty
password. See the keyring Feature's notes.

## Troubleshooting

- Check the configuration: `git config --system --get-regexp '^credential\.'`
- Check the store: `git credential-manager diagnose`
- A user's own `~/.gitconfig` `credential.helper` or `credential.credentialStore` overrides the
  system values.


---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/features/git-credential-manager/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
