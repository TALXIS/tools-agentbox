#!/usr/bin/env bash
# Sets up the installed agent harnesses (Claude Code, GitHub Copilot) with agentbox. Kept at
# https://talxis.com/agentbox-harness for devcontainer configs that still call it; new ones use the
# agentbox Feature, which does this when the container is created.
if command -v agentbox >/dev/null 2>&1; then
    agentbox harness configure
elif command -v dotnet >/dev/null 2>&1; then
    dotnet dnx TALXIS.AgentBox.CLI --yes -- harness configure
else
    echo "Skipping the agent harness setup: agentbox needs .NET 10 (https://dot.net)." >&2
fi
exit 0
