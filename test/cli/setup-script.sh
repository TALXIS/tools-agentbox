#!/usr/bin/env bash
# The setup script in a bare ubuntu:24.04 container, as root, as a Claude Code cloud environment runs
# it: within the time a cloud environment allows, then a second update has nothing to do.
source "$(dirname "$0")/common.sh"

step "setup script"
apt-get update -qq && apt-get install -y -qq curl ca-certificates jq >/dev/null
SECONDS=0
setup
echo "setup took ${SECONDS} s"
[ "${SECONDS}" -le 300 ] || { echo "setup must finish within 5 minutes" >&2; exit 1; }
hash -r
check_ready

step "prerequisite update, twice; the second must change nothing"
agentbox prerequisite update --format text
agentbox prerequisite update --format json > /tmp/second.json
jq -e '[.[] | select(.action == "updated" or .action == "failed")] | length == 0' /tmp/second.json >/dev/null \
    || { cat /tmp/second.json; exit 1; }
