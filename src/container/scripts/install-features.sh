#!/usr/bin/env bash
# Makes this machine ready for coding agents doing Power Platform work: installs .NET 10 if it's
# missing, then lets agentbox install the AgentBox tools and set up the installed agent harnesses.
# For machines dedicated to agents: Claude Code cloud environments, the GitHub Copilot cloud agent
# and the AgentBox image build.
#
#   curl -fsSL https://talxis.com/agentbox-setup | bash
#
# --no-harness  skips the harness setup (an image build; the container does it when it's created)
# --source DIR  also offers the agentbox package in DIR, if there is one (an image build, CI)
#
# Exits with agentbox prerequisite validate's code: 0 when everything is in place.
set -uo pipefail

harness=true source=""
while [ $# -gt 0 ]; do
    case "$1" in
        --no-harness) harness=false ;;
        --source) source="$(cd "$2" && pwd)" || exit 2; shift ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

export HOME="${HOME:-$(eval echo "~$(id -un)")}"
sudo=""
[ "$(id -u)" -eq 0 ] || sudo="sudo -n"

# .NET needs ICU, which bare Debian and Ubuntu images lack.
if ! ldconfig -p | grep -q libicuuc && command -v apt-get >/dev/null; then
    ${sudo} apt-get update -qq \
        && ${sudo} apt-get install -y -qq --no-install-recommends \
            "$(apt-cache search --names-only '^libicu[0-9]+$' | cut -d' ' -f1 | sort -V | tail -1)" >/dev/null \
        || exit 1
fi

# Where and how the dotnet Feature installs it, which then sees it's there.
if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh \
        | ${sudo} bash -s -- --channel 10.0 --install-dir /usr/local/dotnet/10.0 >/dev/null \
        && ${sudo} ln -sfn /usr/local/dotnet/10.0 /usr/local/dotnet/current \
        || exit 1
    export PATH="/usr/local/dotnet/current:${PATH}"
fi
export DOTNET_ROOT="$(dotnet --list-sdks | sed -n 's/^10\..*\[\(.*\)\/sdk\]$/\1/p' | head -1)"

if [ -n "${source}" ] && ls "${source}"/TALXIS.AgentBox.CLI.*.nupkg >/dev/null 2>&1; then
    # A NuGet source for every user, so agentbox here, under sudo, and the agentbox Feature all find
    # it. It wins wherever its version is the highest.
    nuget_config=/etc/opt/NuGet/Config/agentbox-setup.config
    ${sudo} mkdir -p "$(dirname "${nuget_config}")"
    printf '<configuration><packageSources><add key="agentbox-setup" value="%s" /></packageSources></configuration>\n' "${source}" \
        | ${sudo} tee "${nuget_config}" >/dev/null || exit 1
    trap '${sudo} rm -f "${nuget_config}"' EXIT
fi
agentbox() { AGENTBOX_CONTAINER=true dotnet dnx TALXIS.AgentBox.CLI --yes -- "$@"; }

# What failed shows in the output; validate decides the result.
agentbox prerequisite install --format text
[ "${harness}" = false ] || agentbox harness configure --format text
agentbox prerequisite validate --format text
