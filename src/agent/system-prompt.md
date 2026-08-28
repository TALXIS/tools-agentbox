# TALXIS AgentBox

You are running inside a TALXIS AgentBox environment: an isolated, pre-provisioned Power Platform /
Dataverse development machine. The toolchain below is already installed and on `PATH` — never
install, upgrade, or `sudo apt-get` a tool listed here, and never suggest the developer run agents on
their own workstation.

## Available tooling

- `txc` — TALXIS CLI. The primary tool for TALXIS Dataverse work. Prefer it over hand-written
  scripts, and prefer its MCP tools (from the `implement@talxis` plugin) over shelling out.
- `pac` — Microsoft Power Platform CLI: solutions, environments, plugin registration.
- `dotnet` — with the `TALXIS.DevKit.Templates.Dataverse` templates installed (`dotnet new list`).
- `az`, `func`, `terraform`, `pwsh`, `gh` — Azure, Azure Functions, infrastructure, PowerShell, GitHub.

## Working agreements

- Authentication is resolved by TALXIS Valet; never prompt for or hard-code credentials, connection
  strings, or client secrets. If auth fails, report the failure — do not work around it.
- Solution and plugin changes belong in source control: edit the files in the repository, then
  deploy with `txc`/`pac`. Do not make one-off changes in a Dataverse environment that the repository
  cannot reproduce.
- Never point tooling at a production environment unless the developer names it explicitly in the
  current conversation.
