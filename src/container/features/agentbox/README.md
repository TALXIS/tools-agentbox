
# AgentBox CLI (agentbox) (agentbox)

Installs the agentbox CLI. It sets up the installed agent harnesses when the container is created and, on every start, brings the AgentBox tools that follow the latest release and the TALXIS/skills plugin in the harnesses up to date.

## Example Usage

```json
"features": {
    "ghcr.io/TALXIS/tools-agentbox/agentbox:1": {}
}
```

## Options

| Options Id | Description | Type | Default Value |
|-----|-----|-----|-----|


## What it does in a container

When the container is created, `agentbox harness configure` sets up every installed agent harness
(Claude Code, GitHub Copilot). On every start, `agentbox prerequisite update` brings the AgentBox
tools that follow the latest release up to date, and `agentbox harness update` does the same for the
TALXIS/skills plugin, so a long-lived container stays current without a rebuild. Run any of them by
hand at any time; `agentbox --help` lists the rest.

When a development tool attaches, the Feature starts the AgentBox authentication sidecar on
`127.0.0.1:50343` and sets `MSI_ENDPOINT`, `MSI_SECRET=agentbox`, and
`DEFAULT_IDENTITY_CLIENT_ID=04b07795-8ddb-461a-bbee-02f9e1bf7b46`.
The `MSI_SECRET` value is a compatibility placeholder, not a credential. Sign in with
`agentbox auth aad login` before requesting tokens. The sidecar returns delegated user tokens from
the selected account, ignoring identity selectors; it is not an Azure Managed Identity. Azure CLI
does not use `MSI_ENDPOINT`; its current implementation can be pointed at the sidecar by setting
`AZURE_POD_IDENTITY_AUTHORITY_HOST=http://127.0.0.1:50343` for `az login --identity`. This is an
opt-in, best-effort integration, not a stable AgentBox contract; routing was checked with a
synthetic local response, not a real Azure sign-in. The Feature does not set the variable
automatically. Any local process that can reach the endpoint can request tokens as that user, so
use it only in a trusted development environment.


---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/features/agentbox/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
