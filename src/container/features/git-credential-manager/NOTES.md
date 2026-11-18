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

## .NET patch pin

GCM's package pins the exact .NET patch it was built with (e.g. `10.0.14`), so it won't start on an image with an older patch of the same major. The Feature relaxes that pin to `<major>.<minor>.0`; patch releases are compatible.

## Updates

GCM is installed once, when the Feature runs. `agentbox prerequisite update` does not update it,
so a newer GCM release can't break credential access in a running container. To upgrade, rebuild
the image, or run the Feature's install again.
