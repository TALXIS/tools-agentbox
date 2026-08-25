# Tool authentication resolution for sandboxed agents

## Problem

Coding agents run in isolated sandboxes (local devcontainers, GitHub Codespaces, Claude Cloud Environments) with no inbound network path, no browser, and — by policy — no access to credentials on the developer's machine. The tools they drive (`git` against Azure DevOps, the Azure DevOps MCP server, `txc`, `pac`, `az`, future Jira/SharePoint integrations) all need credentials.

Hard constraints:

- **No device-code flow.** It is the primary cross-device consent-phishing vector and is blocked by Conditional Access in security-conscious tenants.
- **Minimal sign-ins.** One lightweight action per session, covering all resources the session needs.
- **Minimal blast radius.** Nothing long-lived or broadly scoped inside the sandbox; per-session scopes, short TTLs, central revocation, full audit.
- **Two audiences.** Internal engineers (hosted service is fine — first-party custody) and enterprise customers (tokens must not transit a third party; self-hosting must be possible).

The immediate driver is internal: engineers cannot use Claude Cloud Environments against Azure DevOps at all (no native integration; the ADO MCP server's interactive and `azcli` auth modes cannot work in the VM).

## Architecture

A session/pairing layer on the company **Security Token Service** ([TALXIS/services-securitytoken](https://github.com/TALXIS/services-securitytoken)), which already brokers tokens in production. Generic authorization-server concepts throughout — no product-specific protocol:

1. **Pairing.** The developer opens the STS portal (normal Entra web sign-in — Conditional Access and MFA apply), selects what the session may access (resources, scopes, TTL), and receives a short one-time code. They paste it into the sandbox session. The direction — *user transfers the code to the device* — is the RFC 10027-recommended shape and inverts the device-code phishing vector.
2. **Session bootstrap.** The sandbox generates a keypair and redeems the code (OpenID4VCI pre-authorized code grant) with a DPoP proof for a **DPoP-bound refresh token** — the session credential. Stolen bytes are useless without the sandbox's private key.
3. **Issuance.** For the rest of the session, tools obtain short-lived (≤1h) per-resource tokens via the standard `refresh_token` grant (+ RFC 8707 `resource`), or derived credentials (e.g. Azure DevOps PATs) via RFC 8693 token exchange. Everything is scoped to the pairing grant and per-tenant policy, audited, and revocable mid-session.

### Trust model and invariants

- **Privilege invariant:** nothing in the sandbox ever holds a credential more powerful than the session grant. Privilege-*reducing* transforms (e.g. ADO access token → scoped PAT) run server-side in provider plugins; sandbox-side *materializers* only format (write a file, run `az login`, serve the git credential protocol, inject an env var). *Plugins downscope; materializers format.*
- **Tenant isolation:** the STS is one multitenant deployment; the Entra tenant (`tid`), derived from claims, is the isolation boundary for sessions, vaults, policy, and audit. Rollout is policy: the workloads surface ships enabled for the internal tenant only.
- **Two client surfaces**, each with its own upstream Entra app registration: `controls` (existing production use) and `workloads` (agent sessions). Separate apps keep consent-scope unions, Conditional Access targeting, and audit (`appid`) cleanly separated.
- **Two credential tiers:** *delegated* (primary — user context; STS holds the upstream refresh token server-side, never the sandbox) and *WIF app-only* (session-scoped OIDC assertions consumed via Entra federated credentials; no refresh tokens exist at all; required for `az`/`pac` and unattended runs).

## Milestones

Backlog lives in [services-securitytoken issues](https://github.com/TALXIS/services-securitytoken/issues), organized by milestone:

| Milestone | Outcome |
|---|---|
| M1 — Sandbox sessions & Azure DevOps | Engineers use Claude Cloud Environments against ADO (git + MCP) via paired sessions |
| M2 — Delegated resources via CLI | Dataverse/Graph/BAP through the same sessions; `txc` auth rework |
| M3 — Workload identity federation tier | App-only assertions; `az`/`pac`; CI and autonomous-agent bootstrap |
| M4 — Multi-tenant productization & self-hosting | Container host, extra transports, DCR, white-label, open-sourcing gate |
| M5 — Non-Microsoft identity providers | Atlassian first, behind the provider abstraction |

See `pairing.md` for the bootstrap protocol and transports, `sts-api.md` for the wire contract, and `workstreams.md` for the per-repo work mapping.
