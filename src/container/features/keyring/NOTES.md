## How it works

AgentBox containers and VMs have no systemd user session and no desktop, so nothing starts a D-Bus
session bus or a keyring. Without one, `txc` and `pac` keep sign-in tokens in a plaintext file and
`pa` doesn't run at all.

Everything goes through one script, `agentbox-keyring-init`. It makes sure the calling user has a
bus at `unix:path=/tmp/agentbox-keyring-<uid>/bus` with an unlocked gnome-keyring Secret Service, and
prints the line that points a shell at it. Every caller uses it the same way:

```sh
eval "$(agentbox-keyring-init)"
```

It's idempotent: when everything is already running it costs one D-Bus round trip. If
`DBUS_SESSION_BUS_ADDRESS` already points at someone else's working bus (a desktop session), it
passes that through and touches nothing.

## Who calls it, and why each is needed

| Caller | Covers |
|---|---|
| `/etc/profile.d/agentbox-keyring.sh` (login shells) | Codespaces and Dev Containers: VS Code reads the environment from a login shell (`userEnvProbe`) and hands it to every terminal, extension and agent. |
| `/etc/bash.bashrc` (interactive shells) | `docker run -it <image> bash` |
| Wrappers in `/usr/local/bin` (`pa`, plus `pac`/`txc` written by `install-features.sh`) | Non-login shells: Claude Code cloud, the Copilot cloud sandbox, `docker run <image> <cmd>` |
| `postStartCommand` (this Feature, and repeated in the power-platform template because the pre-built image doesn't carry Feature hooks) | Restarting the daemons after a container restart, before anything asks for them |

## Security trade-off: empty keyring password

The keyring is unlocked with an empty password, so secrets are protected by file permissions only
(`~/.local/share/keyrings`, `0600`). That's the same protection as the plaintext fallback it
replaces. It suits ephemeral, single-user dev and lab containers; don't rely on it where other users
or processes on the same machine aren't trusted.

## Limits

- Secrets survive restarts (they live in the home directory) but not a Codespace rebuild, the same as
  the plaintext fallback.
- If a repo sets `userEnvProbe: "none"`, VS Code terminals don't get the bus address; `pac`/`txc` then
  fall back to plaintext as before (`pa` still works through its wrapper).

## Troubleshooting

- `~/.local/share/keyrings is locked with a non-empty password`: the keyring was created with a
  different password and can't be unlocked automatically. Delete that directory to start over;
  stored sign-ins are lost.
- Check by hand: `eval "$(agentbox-keyring-init)" && printf x | secret-tool store --label=t k v && secret-tool lookup k v`
