# Workstreams

The backlog of record is the [services-securitytoken issue tracker](https://github.com/TALXIS/services-securitytoken/issues) — all repos' work is tracked there (single review surface), labeled by component. The committed scope is [milestone M1](https://github.com/TALXIS/services-securitytoken/milestone/1); future directions are intentionally not itemized (see README).

## Component → repo mapping

| Label | Repo | Work |
|---|---|---|
| `component:sts` | [services-securitytoken](https://github.com/TALXIS/services-securitytoken) | Session core, grants, DPoP, providers, portal, policy, deployment |
| `component:txc` | [tools-cli](https://github.com/TALXIS/tools-cli) | `txc auth` command group, `OAuthRefreshToken` credential kind |
| `component:agentbox` | [tools-agentbox](https://github.com/TALXIS/tools-agentbox) (this repo) | Devcontainer feature extension, session-start hook, credential materializers, environment docs |
| `component:docs` | mixed | Onboarding, environment setup |

Labels `spike`, `design-decision`, and `existing-code` mark investigations, architecture records, and issues that evolve current INT0014 code (with file-path links that become repo links once the migration issue lands).

## Execution notes

- Spikes ([#18](https://github.com/TALXIS/services-securitytoken/issues/18), [#19](https://github.com/TALXIS/services-securitytoken/issues/19)) and prerequisites (workloads-surface app registration, ADO PAT policy check) go first — they can reshape the PAT issuer design ([#13](https://github.com/TALXIS/services-securitytoken/issues/13)).
- Settled protocol decisions live in the closed record [#2](https://github.com/TALXIS/services-securitytoken/issues/2); the wire contract in `sts-api.md` mirrors it.
- No code is pushed to services-securitytoken until the architects review the backlog; the first implementation issue is the codebase migration ([#1](https://github.com/TALXIS/services-securitytoken/issues/1)) — full history preserved, tip secrets scrubbed.
- Security review (pairing + refresh-token custody) is the M1 exit gate.
- This repo's M1 work: extend the `txc-cli` feature + session-start hook (materializers, egress-allowlist docs) — [#21](https://github.com/TALXIS/services-securitytoken/issues/21), [#22](https://github.com/TALXIS/services-securitytoken/issues/22).
