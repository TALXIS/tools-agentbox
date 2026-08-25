# STS wire contract

Nothing invented on the wire: every client-facing interaction is a registered OAuth grant against discovered endpoints. Full decision record: [services-securitytoken#2](https://github.com/TALXIS/services-securitytoken/issues/2).

## Discovery

RFC 8414 `/.well-known/oauth-authorization-server` (alongside the existing OIDC discovery document). Clients never hardcode paths; the CLI resolves `token_endpoint`, `revocation_endpoint`, `jwks_uri` (and later `registration_endpoint`) from metadata.

## Token endpoint — three grants

| Grant | Purpose | Notes |
|---|---|---|
| `urn:ietf:params:oauth:grant-type:pre-authorized_code` | Pairing-code redemption → session credential | OpenID4VCI grant; optional `tx_code`. **Documented deviation:** used outside its VC-issuance home context; semantics identical; standard OAuth token response. Response RT is DPoP-bound (`cnf.jkt`) |
| `refresh_token` + RFC 8707 `resource` | Per-resource short-lived access tokens | `resource` = real absolute URI (e.g. `https://dev.azure.com/{org}`, Dataverse environment URL). DPoP proof required |
| `urn:ietf:params:oauth:grant-type:token-exchange` (RFC 8693) | Derived credentials | `requested_token_type` = absolute URI under our domain (first: `https://securitytokens.services.talxis.com/token-types/ado-pat`) — never `urn:ietf:params:oauth:*`. Non-bearer responses carry `token_type: "N_A"` per §2.2.1. A future tier adds platform OIDC `subject_token`s (CI/agents) |

## DPoP (RFC 9449)

Required on all grants and session APIs: proof JWT (`htm`/`htu`/`iat`/`jti`), server nonce challenge, `jti` replay cache, `cnf.jkt` match on presented refresh tokens. Conformance oracle for tests: Keycloak's DPoP-bound token-exchange behavior. The txc client must also pass against a Keycloak realm (substitutability test).

## Revocation

RFC 7009 `revocation_endpoint`; revoking the session RT kills the session and cascades to revocable derived credentials (ADO PATs). Portal Revoke uses the same path.

## Grants & policy model

- Session grant details stored as RFC 9396 `authorization_details` (typed per provider; e.g. `{type:"ado", organization, scopes:[work|code|build]}`), each field flagged **enforced** or **advisory** (ADO delegated: scopes enforced via PAT; projects advisory — real project enforcement is the app-only tier's project-scoped service principal).
- Tenant (`tid`, from claims) partitions sessions, vaults, policy, audit. Per-tenant policy: surface enablement (M1: internal tenant only), TTL caps, allowed resources/scopes/token types.

## Error contract

Deterministic, machine-readable errors the CLI maps to remedies: `code_expired` / `code_consumed` / `key_mismatch` (re-pair), `session_revoked` / `session_expired` (re-pair), `resource_not_granted` / `scope_not_granted` (ask user to extend grant via portal), `tenant_not_enabled` (policy), plus standard OAuth error codes on the wire.

## Later (not scheduled)

RFC 7591 dynamic client registration + RFC 9728 protected-resource metadata for the remote MCP facade. Dynamically registered clients are public/untrusted with zero standing grants; registration rate-limited and expiring. Non-goal: hosted remote MCP data proxies for third-party services.
