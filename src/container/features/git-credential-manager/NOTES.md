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
