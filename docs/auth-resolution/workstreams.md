# Workstreams

The backlog of record is the [services-securitytoken issue tracker](https://github.com/TALXIS/services-securitytoken/issues) — all repos' work is tracked there (single review surface), labeled by component and organized in [milestones](https://github.com/TALXIS/services-securitytoken/milestones) M1–M5.

## Component → repo mapping

| Label | Repo | Work |
|---|---|---|
| `component:sts` | [services-securitytoken](https://github.com/TALXIS/services-securitytoken) | Session core, grants, DPoP, providers, portal, policy, deployment |
| `component:txc` | [tools-cli](https://github.com/TALXIS/tools-cli) | `txc auth` command group, `OAuthRefreshToken` credential kind, M2 auth rework |
| `component:agentbox` | [tools-agentbox](https://github.com/TALXIS/tools-agentbox) (this repo) | Devcontainer feature extension, session-start hook, credential materializers, environment docs |
| `component:clients` | TBD | Connector MCP facade client side, VS Code extension (M4) |
| `component:docs` | mixed | Onboarding, self-hosting, environment setup |

Labels `spike`, `design-decision`, and `existing-code` mark investigations, architecture records for review, and issues that evolve current INT0014 code (with file-path links that become repo links once the migration issue lands).

## Execution notes

- M1 order: spikes ([#18](https://github.com/TALXIS/services-securitytoken/issues/18), [#19](https://github.com/TALXIS/services-securitytoken/issues/19)) and prerequisites (app registration, ADO PAT policy check) first — they can reshape the PAT issuer design. Architecture review gate: [#2](https://github.com/TALXIS/services-securitytoken/issues/2).
- No code is pushed to services-securitytoken until the architects review the backlog; the first implementation issue is the codebase migration ([#1](https://github.com/TALXIS/services-securitytoken/issues/1)).
- Security review (pairing + refresh-token custody) is the M1 exit gate; the WIF app-only tier (M3) is the documented fallback if the delegated ADO path hits a policy wall.
- This repo's M1 issue: extend the `txc-cli` feature + session-start hook (materializers, egress-allowlist docs) — tracked as [#21](https://github.com/TALXIS/services-securitytoken/issues/21) and [#22](https://github.com/TALXIS/services-securitytoken/issues/22).
