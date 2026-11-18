# agentbox

One command to make any machine or container ready for coding agents working on Power Platform /
Dataverse: the tools they need, kept current, and the agent harnesses set up with the
[TALXIS/skills](https://github.com/TALXIS/skills) plugin.

```bash
dnx TALXIS.DevKit.AgentBox.CLI --yes prerequisite validate     # what's missing or outdated
dnx TALXIS.DevKit.AgentBox.CLI --yes prerequisite update       # in a sandbox: bring it up to date, in place
```

`dnx` comes with the .NET 10 SDK and always runs the latest release. `agentbox --help` describes
every command.

**In a container or cloud VM** it installs the full AgentBox tool set from the same pinned
devcontainer Features the pre-built image uses (as root, through `sudo` if needed), and keeps it
current. **On your own machine** it only checks: it shows what's missing and the command to install
it, and sets up the agent harnesses. Agents are better off in a sandbox anyway.

| Exit code | Meaning |
|---|---|
| 0 | Done |
| 1 | Something failed |
| 2 | Invalid input |
| 3 | `validate`: not ready (something missing or unhealthy) |

Results go to stdout (text in a terminal, JSON when piped), logs to stderr. No telemetry.

## Local Azure authentication

Sign in once with device code, then start the local token endpoint:

```bash
agentbox auth aad login
agentbox auth sidecar start
agentbox auth sidecar status
```

Use `agentbox auth aad login --tenantId <tenant-id>` to select a tenant. The `entra` alias is also
available (`agentbox auth entra login`). If a resource requires consent, authenticate for it
explicitly and retry:

```bash
agentbox auth aad login --resource https://graph.microsoft.com/
```

The endpoint listens only on `127.0.0.1:50343`:

```bash
curl 'http://localhost:50343/metadata/identity/oauth2/token?resource=https%3A%2F%2Fmanagement.azure.com%2F' \
  -H 'Metadata: true'
```

As an alternative to `Metadata: true`, clients can send a nonempty `Secret` header with any value.
The value is not checked and does not authenticate the caller.

The `resource` can be an absolute URI or an application ID GUID. Its value is preserved in the
response; MSAL requests the corresponding `<resource>/.default` scope.

It also accepts the Cloud Shell-style POST form used by Azure Identity SDKs:

```bash
curl -X POST 'http://localhost:50343/metadata/identity/oauth2/token' \
  -H 'Metadata: true' \
  -d 'resource=https%3A%2F%2Fmanagement.azure.com%2F'
```

The AgentBox devcontainer Feature sets `MSI_ENDPOINT`, the compatibility placeholder
`MSI_SECRET=agentbox`, and `DEFAULT_IDENTITY_CLIENT_ID=04b07795-8ddb-461a-bbee-02f9e1bf7b46`,
and starts the sidecar when a development tool attaches. Outside that environment, set them for
the client process:

```bash
MSI_ENDPOINT=http://localhost:50343/metadata/identity/oauth2/token MSI_SECRET=agentbox DEFAULT_IDENTITY_CLIENT_ID=04b07795-8ddb-461a-bbee-02f9e1bf7b46 your-application
```

This endpoint returns delegated **user** tokens from the active AgentBox account; they are not
Managed Identity service-principal tokens. Identity selectors in requests are ignored, including
the client ID. The current supported protocol target is Azure Identity
SDKs that honor `MSI_ENDPOINT`. Azure CLI `az login --identity` does not use `MSI_ENDPOINT`; the
current Azure CLI/MSAL Python managed-identity path can instead be directed to the sidecar with:

```bash
agentbox auth aad login --resource https://management.core.windows.net/
AZURE_POD_IDENTITY_AUTHORITY_HOST=http://127.0.0.1:50343 az login --identity
```

This is an opt-in, best-effort integration with Azure CLI's current implementation, not a stable
AgentBox contract. Its endpoint routing was checked against a synthetic local response; no real
Azure sign-in was performed, and AgentBox does not set this variable automatically. Consent,
Conditional Access, or MFA can require running the device-code login again for a resource; the
sidecar never starts an interactive flow.

The localhost endpoint is intentionally unauthenticated. Any local process that can reach it can
request tokens as the signed-in user. Use it only in a trusted local development environment, and
stop it when no longer needed:

```bash
agentbox auth sidecar stop
```
