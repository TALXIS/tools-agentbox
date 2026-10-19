## What it does in a container

When the container is created, `agentbox harness configure` sets up every installed agent harness
(Claude Code, GitHub Copilot). On every start, `agentbox prerequisite update` brings the AgentBox
tools that follow the latest release up to date, so a long-lived container stays current without a
rebuild. Run either by hand at any time; `agentbox --help` lists the rest.
