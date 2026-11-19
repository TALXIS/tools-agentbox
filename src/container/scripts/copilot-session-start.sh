#!/usr/bin/env bash
# Makes a GitHub Copilot cloud sandbox ready for Power Platform work. A repository runs it from a
# sessionStart hook, its only AgentBox configuration (.github/hooks/agentbox.json):
#
#   { "version": 1, "hooks": { "sessionStart": [ { "type": "command", "timeoutSec": 30,
#     "bash": "curl -fsSL https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/container/scripts/copilot-session-start.sh | bash" } ] } }
#
# Cloud sandboxes run neither setup steps nor dev containers, and start every session from GitHub's
# own image. So this script:
#   1. puts the TALXIS skills in ~/.copilot/skills and prints them as session context, at once;
#   2. prepares the tools in the background. On Ubuntu 24.04 x64 it restores the published AgentBox
#      image's layers straight from ghcr.io (about a minute); anywhere else, or if that fails, it
#      runs the AgentBox setup script (about 5 minutes).
# It writes /tmp/agentbox/done when the tools are ready, /tmp/agentbox/failed if they aren't; the log
# is /tmp/agentbox/setup.log. Every host it uses is on Copilot's recommended firewall allowlist.
#
# AGENTBOX_ROOT        restore into this directory instead of / (tests)
# AGENTBOX_IMAGE       the image to restore (default talxis/tools-agentbox/image:latest)
# AGENTBOX_SKILLS_URL  where the TALXIS/skills archive comes from (tests)
set -uo pipefail

STATE=/tmp/agentbox
ROOT="${AGENTBOX_ROOT:-/}"
IMAGE="${AGENTBOX_IMAGE:-talxis/tools-agentbox/image:latest}"
SETUP_URL=https://raw.githubusercontent.com/TALXIS/tools-agentbox/master/src/container/scripts/install-features.sh
SKILLS_URL="${AGENTBOX_SKILLS_URL:-https://codeload.github.com/TALXIS/skills/tar.gz/refs/heads/master}"
SKILLS_DIR="${HOME:-/root}/.copilot/skills"

log() { echo "[$(date -u +%T)] $*"; }

# ── Restore the image's layers ────────────────────────────────────────────────────────────────
# The image's first layer is the Ubuntu base the sandbox already has; the rest are the apt packages
# and the tools. Each layer is downloaded in parallel ranges (one stream is several times slower),
# checked against its digest, and unpacked over the sandbox, leaving its accounts, network and
# package database alone.
registry_json() { curl -fsSL --retry 3 -H "Authorization: Bearer ${TOKEN}" -H "Accept: $2" "https://ghcr.io/v2/${REPO}/$1"; }

fetch_layer() { # digest size target
    local digest="$1" size="$2" target="$3" url parts=1 part i start end pids=()
    url="$(curl -fsS -o /dev/null -w '%{redirect_url}' -H "Authorization: Bearer ${TOKEN}" "https://ghcr.io/v2/${REPO}/blobs/${digest}")"
    [ -n "${url}" ] || url="https://ghcr.io/v2/${REPO}/blobs/${digest}"
    [ "${size}" -gt 67108864 ] && parts=8
    part=$(( (size + parts - 1) / parts ))
    for i in $(seq 0 $((parts - 1))); do
        start=$((i * part)); end=$((start + part - 1)); [ "${end}" -ge "${size}" ] && end=$((size - 1))
        curl -fsS --retry 3 -H "Authorization: Bearer ${TOKEN}" -r "${start}-${end}" -o "${target}.${i}" "${url}" & pids+=($!)
    done
    for i in "${pids[@]}"; do wait "${i}" || return 1; done
    [ "sha256:$(cat $(seq -f "${target}.%g" 0 $((parts - 1))) | sha256sum | cut -d' ' -f1)" = "${digest}" ] || { log "digest mismatch for ${digest}"; return 1; }
    cat $(seq -f "${target}.%g" 0 $((parts - 1))) > "${target}" && rm -f "${target}".[0-9]*
}

restore_image() {
    . /etc/os-release 2>/dev/null
    if [ "${ID:-}" != ubuntu ] || [ "${VERSION_ID:-}" != 24.04 ] || [ "$(uname -m)" != x86_64 ]; then
        log "restore needs Ubuntu 24.04 x64, this is ${PRETTY_NAME:-unknown} $(uname -m)"; return 1
    fi
    command -v python3 >/dev/null || { log "restore needs python3"; return 1; }
    [ "$(id -u)" -eq 0 ] || { log "restore needs root"; return 1; }

    REPO="${IMAGE%%:*}"; local tag="${IMAGE##*:}" index manifest config layers unzip=gzip n=0 digest size
    TOKEN="$(curl -fsSL --retry 3 "https://ghcr.io/token?scope=repository:${REPO}:pull" | python3 -c 'import sys,json; print(json.load(sys.stdin)["token"])' 2>/dev/null)" || { log "can't read ${IMAGE} from ghcr.io"; return 1; }
    index="$(registry_json "manifests/${tag}" 'application/vnd.oci.image.index.v1+json,application/vnd.docker.distribution.manifest.list.v2+json')" || return 1
    digest="$(python3 -c 'import sys,json; print(next(m["digest"] for m in json.loads(sys.argv[1])["manifests"] if m.get("platform",{}).get("architecture")=="amd64"))' "${index}")" || return 1
    manifest="$(registry_json "manifests/${digest}" 'application/vnd.oci.image.manifest.v1+json,application/vnd.docker.distribution.manifest.v2+json')" || return 1
    config="$(registry_json "blobs/$(python3 -c 'import sys,json; print(json.loads(sys.argv[1])["config"]["digest"])' "${manifest}")" '*/*')" || return 1
    layers="$(python3 -c 'import sys,json; [print(l["digest"], l["size"]) for l in json.loads(sys.argv[1])["layers"][1:] if l["size"] > 32]' "${manifest}")"
    log "restoring ${IMAGE} (${digest}): $(echo "${layers}" | awk '{s+=$2} END {printf "%.0f MB", s/1048576}')"
    command -v pigz >/dev/null && unzip=pigz

    while read -r digest size; do
        n=$((n + 1))
        fetch_layer "${digest}" "${size}" "${STATE}/layer${n}" || return 1
        log "layer ${n} downloaded ($((size / 1048576)) MB), unpacking"
        "${unzip}" -dc "${STATE}/layer${n}" | tar -x -C "${ROOT}" --keep-directory-symlink --no-overwrite-dir \
            --wildcards --no-anchored --exclude='.wh.*' \
            --anchored --exclude='etc/passwd*' --exclude='etc/group*' --exclude='etc/shadow*' \
            --exclude='etc/gshadow*' --exclude='etc/hosts' --exclude='etc/hostname' \
            --exclude='etc/resolv.conf' --exclude='etc/ld.so.cache' --exclude='var/lib/dpkg*' \
            --exclude='var/lib/apt*' --exclude='var/cache*' --exclude='var/log*' --exclude='tmp*' \
            --exclude='root/.bashrc' --exclude='root/.profile' || return 1
        rm -f "${STATE}/layer${n}"
    done <<< "${layers}"
    ldconfig -r "${ROOT}" 2>/dev/null || true

    # The image's environment, for shells that aren't children of this one.
    python3 -c 'import sys,json
for e in json.loads(sys.argv[1])["config"].get("Env", []):
    k, _, v = e.partition("=")
    if k not in ("PATH", "DEBIAN_FRONTEND", "HOME"): print(f"export {k}={v}")' "${config}" > "${ROOT%/}/etc/profile.d/agentbox-env.sh"
    sed 's/^export //' "${ROOT%/}/etc/profile.d/agentbox-env.sh" >> "${ROOT%/}/etc/environment"
}

prepare_tools() {
    local start=${SECONDS}
    if restore_image; then
        log "tools restored in $((SECONDS - start)) s"
    else
        log "restore didn't work, running the AgentBox setup script instead"
        curl -fsSL --retry 3 "${SETUP_URL}" | bash || { log "setup failed"; touch "${STATE}/failed"; return 1; }
        log "tools installed in $((SECONDS - start)) s"
    fi
    PATH="/usr/local/bin:${PATH}" agentbox prerequisite validate --format text 2>&1 | tail -25
    touch "${STATE}/done"
}

# ── Session start ────────────────────────────────────────────────────────────────────────────
# Once per sandbox: a resumed session keeps what was prepared. The preparation runs in its own
# session, so it outlives the hook; it gets this script's functions, as `curl | bash` leaves no file.
if mkdir "${STATE}" 2>/dev/null; then
    mkdir -p "${SKILLS_DIR}"
    curl -fsSL --max-time 20 "${SKILLS_URL}" \
        | tar -xz -C "${SKILLS_DIR}" --strip-components=4 --wildcards 'skills-master/plugins/implement/skills/*' 2>/dev/null
    export STATE ROOT IMAGE SETUP_URL
    export -f log registry_json fetch_layer restore_image prepare_tools
    nohup setsid bash -c prepare_tools >"${STATE}/setup.log" 2>&1 </dev/null &
fi

skills="$(for f in "${SKILLS_DIR}"/*/SKILL.md; do [ -f "${f}" ] && printf -- '- %s: %s\n' "$(sed -n 's/^name: *//p' "${f}" | head -1)" "${f}"; done)"
python3 - "${skills}" <<'PY' 2>/dev/null || printf '{"additionalContext": "AgentBox: the Power Platform tools are being prepared in the background; wait for /tmp/agentbox/done before using them."}\n'
import json, sys
skills = sys.argv[1].strip()
text = ("AgentBox is preparing the Power Platform tools (.NET 10, txc, pac, pa, pwsh, az, Playwright) "
        "in the background, which takes one to five minutes. Read and plan meanwhile; before you run any "
        "of them, wait until /tmp/agentbox/done exists (check every 20 seconds). If /tmp/agentbox/failed "
        "appears instead, show the end of /tmp/agentbox/setup.log, including any host the firewall blocked.")
if skills:
    text += ("\n\nTALXIS skills for Power Platform work. Read the matching SKILL.md before you start such a task:\n" + skills)
print(json.dumps({"additionalContext": text}))
PY
