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

    local local_package=""
    if [ -n "${source}" ] && ls "${source}"/TALXIS.DevKit.AgentBox.CLI.*.nupkg >/dev/null 2>&1; then
        local_package="$(find "${source}" -maxdepth 1 -type f -name 'TALXIS.DevKit.AgentBox.CLI.*.nupkg' -print -quit)"
        # Source mapping makes this exact package ID come only from the build artifact. Other tool
        # dependencies still restore from nuget.org.
        local nuget_config=/etc/opt/NuGet/Config/agentbox-setup.config
        ${sudo} mkdir -p "$(dirname "${nuget_config}")"
        printf '<configuration><packageSources><clear /><add key="agentbox-setup" value="%s" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><clear /><packageSource key="agentbox-setup"><package pattern="TALXIS.DevKit.AgentBox.CLI" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>\n' "${source}" \
            | ${sudo} tee "${nuget_config}" >/dev/null || exit 1
        trap "${sudo} rm -f '${nuget_config}'" EXIT
    fi

    # This machine is dedicated to agents, whether or not agentbox would detect it as a container.
    AGENTBOX_CONTAINER=true dotnet dnx TALXIS.DevKit.AgentBox.CLI --yes -- setup --format text "${args[@]}"

    if [ -n "${local_package}" ]; then
        local expected_version installed_version assembly expected_hash installed_hash
        expected_version="$(basename "${local_package}" .nupkg | sed 's/^TALXIS\.DevKit\.AgentBox\.CLI\.//')"
        installed_version="$(dotnet tool list --tool-path /usr/local/share/agentbox \
            | awk 'tolower($1) == "talxis.devkit.agentbox.cli" { print $2; exit }')"
        assembly="/usr/local/share/agentbox/.store/talxis.devkit.agentbox.cli/${expected_version}/talxis.devkit.agentbox.cli/${expected_version}/tools/net10.0/any/TALXIS.DevKit.AgentBox.CLI.dll"
        if [ "${installed_version}" != "${expected_version}" ] || [ ! -f "${assembly}" ] || ! command -v unzip >/dev/null 2>&1; then
            echo "AgentBox CLI ${expected_version} from ${local_package} was not installed from the local artifact" >&2
            exit 1
        fi
        expected_hash="$(unzip -p "${local_package}" tools/net10.0/any/TALXIS.DevKit.AgentBox.CLI.dll | sha256sum | awk '{print $1}')"
        installed_hash="$(sha256sum "${assembly}" | awk '{print $1}')"
        if [ -z "${expected_hash}" ] || [ "${installed_hash}" != "${expected_hash}" ]; then
            echo "Installed AgentBox CLI does not match ${local_package}" >&2
            exit 1
        fi
    fi
}

main "$@"
