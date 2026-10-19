#!/usr/bin/env bash
# Makes this machine ready for coding agents doing Power Platform work. For Linux machines dedicated
# to agents: Claude Code cloud environments, the GitHub Copilot cloud agent and the AgentBox image
# build. It only makes .NET 10 available; `agentbox setup` does the rest and decides the exit code.
#
#   curl -fsSL https://talxis.com/agentbox-setup | bash
#
# --no-harness  skips the harness setup (an image build; the container does it when it's created)
# --source DIR  also offers the agentbox package in DIR, if there is one (an image build, CI)
set -uo pipefail

# All in a function, so a download cut short runs nothing.
main() {
    local source="" args=()
    while [ $# -gt 0 ]; do
        case "$1" in
            --source) source="$(cd "$2" && pwd)" || exit 2; shift ;;
            *) args+=("$1") ;;
        esac
        shift
    done

    # It installs system-wide. Setup scripts and image builds have no terminal; a person at one confirms.
    if [ -t 2 ] && [ -r /dev/tty ]; then
        read -r -p "This installs the AgentBox tools system-wide, for a machine dedicated to coding agents. Continue? [y/N] " answer </dev/tty
        [[ "${answer}" =~ ^[Yy] ]] || exit 1
    fi

    export HOME="${HOME:-$(getent passwd "$(id -un)" | cut -d: -f6)}"
    command -v apt-get >/dev/null || { echo "AgentBox supports Debian and Ubuntu." >&2; exit 1; }
    local sudo=""
    [ "$(id -u)" -eq 0 ] || sudo="sudo -n"

    # .NET needs ICU, which bare images lack.
    if ! compgen -G '/usr/lib/*/libicuuc.so.*' >/dev/null; then
        ${sudo} apt-get update -qq \
            && ${sudo} apt-get install -y -qq --no-install-recommends \
                "$(apt-cache search --names-only '^libicu[0-9]+$' | cut -d' ' -f1 | sort -V | tail -1)" >/dev/null \
            || exit 1
    fi

    # Where the dotnet Feature installs it, so agentbox keeps this one instead of installing it again.
    if ! grep -q '^10\.' <<<"$(dotnet --list-sdks 2>/dev/null)"; then
        curl -fsSL https://dot.net/v1/dotnet-install.sh \
            | ${sudo} bash -s -- --channel 10.0 --install-dir /usr/local/dotnet/10.0 >/dev/null \
            && ${sudo} ln -sfn /usr/local/dotnet/10.0 /usr/local/dotnet/current \
            || exit 1
        export PATH="/usr/local/dotnet/current:${PATH}"
    fi
    export DOTNET_ROOT="$(dotnet --list-sdks | sed -n 's/^10\..*\[\(.*\)\/sdk\]$/\1/p' | head -1)"

    if [ -n "${source}" ] && ls "${source}"/TALXIS.AgentBox.CLI.*.nupkg >/dev/null 2>&1; then
        # A NuGet source for every user, so agentbox here, under sudo, and the agentbox Feature all
        # find it. It wins wherever its version is the highest.
        local nuget_config=/etc/opt/NuGet/Config/agentbox-setup.config
        ${sudo} mkdir -p "$(dirname "${nuget_config}")"
        printf '<configuration><packageSources><add key="agentbox-setup" value="%s" /></packageSources></configuration>\n' "${source}" \
            | ${sudo} tee "${nuget_config}" >/dev/null || exit 1
        trap "${sudo} rm -f '${nuget_config}'" EXIT
    fi

    # This machine is dedicated to agents, whether or not agentbox would detect it as a container.
    AGENTBOX_CONTAINER=true dotnet dnx TALXIS.AgentBox.CLI --yes -- setup --format text "${args[@]}"
}

main "$@"
