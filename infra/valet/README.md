# TALXIS Valet infrastructure (placeholder)

This is where **TALXIS Valet**'s Terraform root module lands, with its own Terraform state key —
matching the split boundary described in [`src/valet/README.md`](../../src/valet/README.md), so
Valet's infrastructure can be provisioned and eventually extracted independently of the rest of
AgentBox's infrastructure under `infra/`.

It provisions an Azure Container Apps environment and app, a storage account (the single substrate
for all Valet state), Key Vault, a user-assigned managed identity, and the custom domain and TLS
for the issuer hostname.

No Terraform lives here yet. This directory exists so the target layout is visible ahead of that
provisioning work.
