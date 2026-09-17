# TALXIS Valet (placeholder)

This is where **TALXIS Valet** — the token broker that pairs sandboxed coding agents with
credentials for external tools (e.g. Azure DevOps) — lands as application code.

It is authored inside this repo behind a **split boundary**: its own solution and namespaces, its
own path-filtered CI workflows, and HTTP-contract-only interaction with the rest of AgentBox, so it
can be extracted into its own repository later without a rewrite. That boundary is CI-enforced once
the guardrail work lands.

Valet's Terraform lives separately at [`infra/valet/`](../../infra/valet/), with its own state key.

No code lives here yet. The design record and the work backlog are the issues in
`TALXIS/services-securitytoken`; the design documents that used to accompany them were removed as
superseded, so the issues are the single source of truth.
