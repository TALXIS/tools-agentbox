# Tool authentication resolution

Design documentation and the work backlog for brokering tool authentication to sandboxed coding agents live in the **TALXIS Valet** (token broker) repository:

- **Design docs:** [TALXIS/services-securitytoken](https://github.com/TALXIS/services-securitytoken) (`README.md`, `docs/pairing.md`, `docs/token-endpoint.md`)
- **Backlog:** [milestone M1 — Sandbox sessions & Azure DevOps](https://github.com/TALXIS/services-securitytoken/milestone/1); this repo's work items are labeled `component:agentbox`

AgentBox's role: the `txc-cli` devcontainer feature and session-start hooks install the pairing helper and credential materializers (git credential helper for `dev.azure.com`, ADO MCP server launch wrapper), and the environment docs cover the egress allowlist (`dev.azure.com`, `login.microsoftonline.com`, `valet.services.talxis.com`).
