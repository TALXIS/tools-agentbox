
# AgentBox CLI (agentbox) (agentbox)

Installs the agentbox CLI. It sets up the installed agent harnesses when the container is created and brings the AgentBox tools that follow the latest release up to date on every start.

## Example Usage

```json
"features": {
    "ghcr.io/TALXIS/tools-agentbox/agentbox:1": {}
}
```



## What it does in a container

When the container is created, `agentbox harness configure` sets up every installed agent harness
(Claude Code, GitHub Copilot). On every start, `agentbox prerequisite update` brings the AgentBox
tools that follow the latest release up to date, so a long-lived container stays current without a
rebuild. Run either by hand at any time; `agentbox --help` lists the rest.


---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/container/features/agentbox/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
