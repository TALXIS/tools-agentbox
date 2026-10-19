#!/usr/bin/env bash
# Sets up the installed agent harnesses (Claude Code, GitHub Copilot) with agentbox. Kept at
# https://talxis.com/agentbox-harness for devcontainer configs that still call it; new ones use the
# agentbox Feature, which does this when the container is created. Never fails the caller.
if command -v agentbox >/dev/null 2>&1; then
    agentbox harness configure || echo "WARNING: agentbox couldn't set up every agent harness (see above)." >&2
elif grep -q '^10\.' <<<"$(dotnet --list-sdks 2>/dev/null)"; then
    dotnet dnx TALXIS.AgentBox.CLI --yes -- harness configure \
        || echo "WARNING: agentbox couldn't set up every agent harness (see above)." >&2
else
    echo "WARNING: skipping the agent harness setup: agentbox needs the .NET 10 SDK (https://dot.net)." >&2
fi
exit 0
