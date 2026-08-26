# TALXIS Valet infrastructure (placeholder)

This is where **TALXIS Valet**'s Terraform root module lands, with its own Terraform state key —
matching the split boundary described in [`src/valet/README.md`](../../src/valet/README.md), so
Valet's infra can be provisioned and eventually extracted independently of the rest of AgentBox's
infrastructure under `infra/`.

No Terraform lives here yet. This directory exists so the target repository layout (tracked in
[issue #32](https://github.com/TALXIS/tools-agentbox/issues/32)) is visible ahead of that
provisioning work.
