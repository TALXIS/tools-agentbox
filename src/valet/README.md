# TALXIS Valet (placeholder)

This is where **TALXIS Valet** — the token broker that pairs sandboxed coding agents with
credentials for external tools (e.g. Azure DevOps) — lands as application code. It is authored
inside this repo but built as a **split boundary**: its own solution/namespaces, its own
path-filtered CI workflows, and HTTP-contract-only interaction with the rest of AgentBox (a
constraint intended to be CI-enforced once the split boundary work lands), so it can be extracted
into its own product/repo later without a rewrite.

Valet's Terraform lives separately at [`infra/valet/`](../../infra/valet/), and its docs will live
at `docs/valet/` once they migrate — see [`docs/auth-resolution/README.md`](../../docs/auth-resolution/README.md)
for where the design docs and backlog currently live
([TALXIS/services-securitytoken](https://github.com/TALXIS/services-securitytoken)).

No application code lives here yet. This directory exists so the target repository layout
(tracked in [issue #32](https://github.com/TALXIS/tools-agentbox/issues/32)) is visible ahead of
that adoption work.
