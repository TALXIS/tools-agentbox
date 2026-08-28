#!/usr/bin/env bash
# Smoke test for src/agent/ (the one place agent behaviour is configured) and the script that applies
# it. Runs test/agent/assert.sh in a throwaway container, since the assertions write to system paths
# (/etc/claude-code, /etc/skel, /usr/local/share/agentbox).
set -e

echo "=== Smoke test: agent config (src/agent) ==="

docker run --rm -v "$(pwd):/repo:ro" ubuntu:24.04 bash -c '
    set -e
    apt-get update -qq >/dev/null && apt-get install -y -qq jq >/dev/null
    export HOME=/root REPO=/repo
    bash /repo/test/agent/assert.sh
'

echo "=== agent config smoke test PASSED ==="
