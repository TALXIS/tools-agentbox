
# Secret Service keyring (gnome-keyring) (secret-service)

Starts a D-Bus session bus and an unlocked gnome-keyring Secret Service on every container start, so CLIs that store credentials through libsecret (txc, pac, pa) use the OS credential vault instead of a plaintext fallback. The keyring has an empty password.

## Example Usage

```json
"features": {
    "ghcr.io/TALXIS/tools-agentbox/secret-service:1": {}
}
```



## How it works

The container has no systemd and no desktop session, so nothing starts a D-Bus session bus or a
keyring on its own. This Feature installs `agentbox-keyring-init`, which:

1. starts `dbus-daemon --session` on `unix:path=/tmp/agentbox-dbus-<uid>/bus` if it isn't running;
2. starts `gnome-keyring-daemon --unlock --components=secrets` with an empty password if
   `org.freedesktop.secrets` isn't owned yet, or replaces it if it is running but locked (for
   example, because a client D-Bus-activated it before the unlock).

It is idempotent and runs from:

- `postStartCommand`, since the daemons don't survive a container restart;
- `/etc/profile.d/agentbox-keyring.sh` (login shells) and `/etc/bash.bashrc` (interactive shells),
  which also export `DBUS_SESSION_BUS_ADDRESS`;
- the `pa`, `pac` and `txc` wrappers in `/usr/local/bin`, so non-login shells (agent tool calls,
  `docker run <image> <command>`) work without any setup.

Secrets live in `~/.local/share/keyrings` and survive restarts as long as the home directory does.

## Security trade-off: empty keyring password

The keyring is unlocked with an empty password, so it protects secrets only with file permissions
(the keyring files are `0600`). That is the same protection as the plaintext fallback caches that
`txc` and `pac` use without a keyring, and it is meant for ephemeral, single-user dev and lab
containers. Don't rely on it where other users or processes on the same machine aren't trusted.

## Troubleshooting

- `the keyring ... is locked with a non-empty password`: the keyring was created with a different
  password and can't be unlocked automatically. Delete `~/.local/share/keyrings` to start over;
  stored sign-ins are lost.
- Check the state by hand: `agentbox-keyring-init && secret-tool store --label=test k v <<< x && secret-tool lookup k v`.


---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/features/secret-service/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
