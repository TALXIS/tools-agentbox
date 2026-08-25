# Session pairing

One bootstrap core, multiple transports. The core never changes; transports only differ in how the one-time code (or subject token) reaches the sandbox.

## Core

1. Sandbox helper (`txc auth`) generates an ephemeral keypair; its thumbprint (`jkt`) identifies the instance.
2. A one-time pairing code is created for the signed-in user with a grant: resources, scopes, TTL — within per-tenant policy. The code is high-entropy, single-use, minutes-lived, rate-limited at redemption, and bound at creation to the instance `jkt` (server-side policy on an opaque value).
3. The sandbox redeems the code at the token endpoint — `grant_type=urn:ietf:params:oauth:grant-type:pre-authorized_code` with a DPoP proof — and receives a **DPoP-bound refresh token** (the session credential) plus a first access token.
4. All later issuance requires a DPoP proof from the same key. Sessions end by TTL, portal/API revocation, or upstream revocation (password reset, CA reevaluation) surfacing as a re-pair prompt.

### RFC 10027 compliance (BCP: cross-device flows)

- Direction: **user transfers the code to the consumption device** — resists cross-device consent phishing (the sandbox never shows the user anything to approve).
- Code entropy, single-use, short expiry, redemption rate limits.
- Context display at creation (what the session will access, for how long) and in the active-session list (instance fingerprint), with one-click revocation.
- The code transits the chat transcript in the Claude Cloud case; it is single-use, expires in minutes, and is useless without the sandbox private key. Onboarding docs teach what a legitimate pairing request looks like.

## Transports (runtime × harness)

| # | Transport | Where | Human steps per session |
|---|---|---|---|
| 1 | **Manual code** (M1) | Universal fallback; Claude Cloud Environments | Open portal, sign in (SSO), pick grant, paste code |
| 2 | **Remote MCP facade / connector** (M4) | Claude Cloud with claude.ai connector | None after one-time connector authorization; the `pair_session(jkt)` tool returns **only** a jkt-bound one-time code — never tokens (tool results land in transcripts). Per-tenant policy: standing-grant defaults vs. portal step-up; OTP-only enforceable |
| 3 | **VS Code extension** (M4) | Codespaces / devcontainers with VS Code | None (editor's Microsoft auth session pairs automatically) |
| 4 | **Host hook** (M1-adjacent) | Local docker devcontainer, CLI-only | None (devcontainer `initializeCommand` pairs on the host, which has a browser) |
| 5 | **Platform OIDC exchange** (M3) | GitHub Actions, Copilot coding agent, CI | None; the runner's platform OIDC token is the RFC 8693 `subject_token`; app-only tier |

## Refresh and failure semantics

- Consumers cache access tokens in memory and re-request near expiry; the session credential itself needs no client-side refresh handling (`refresh_token` grant each time, DPoP-proved).
- Materializer specifics: `AZURE_FEDERATED_TOKEN_FILE` is rewritten atomically; `az` needs a re-login loop (it does not re-read the file); git credential helper is queried per operation (naturally fresh); ADO MCP server rotation behavior is a tracked spike.
- STS unreachable: tools ride cached tokens (≤1h grace), helper retries with backoff.
- Revocation latency ceiling = remaining lifetime of already-issued tokens (≤1h); tighter requires shorter upstream token lifetimes (tenant policy trade-off, documented).
