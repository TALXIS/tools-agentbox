#!/bin/bash
# Claude Code cloud environment setup script. Paste into the "Setup script" field at
# claude.ai/admin-settings/cloud-environments (Network access: Custom, allow cli.github.com
# and aka.ms in addition to the defaults). Mirrors src/images/power-platform/Dockerfile —
# keep the two in sync when tools change there.
set -uo pipefail

curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet

(
  curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
    | gpg --dearmor -o /usr/share/keyrings/githubcli-archive-keyring.gpg \
  && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
    > /etc/apt/sources.list.d/github-cli.list \
  && apt-get update && apt-get install -y gh
) || true &

( curl -fsSL https://aka.ms/InstallAzureCLIDeb | bash && az extension add --name azure-devops --yes ) || true &

(
  ARCH=$(dpkg --print-architecture)
  PWSH_VERSION=$(curl -fsSL https://api.github.com/repos/PowerShell/PowerShell/releases/latest \
    | grep '"tag_name"' | sed 's/.*"v\([^"]*\)".*/\1/')
  if [ "$ARCH" = "amd64" ]; then
    curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v${PWSH_VERSION}/powershell_${PWSH_VERSION}-1.deb_amd64.deb" -o /tmp/powershell.deb \
      && dpkg -i /tmp/powershell.deb; apt-get install -f -y
  else
    mkdir -p /opt/microsoft/powershell/7
    curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v${PWSH_VERSION}/powershell-${PWSH_VERSION}-linux-arm64.tar.gz" \
      | tar -xz -C /opt/microsoft/powershell/7
    ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh
  fi
) || true &

(
  curl -fsSL https://apt.releases.hashicorp.com/gpg | gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg \
  && echo "deb [signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] https://apt.releases.hashicorp.com $(lsb_release -cs) main" \
    > /etc/apt/sources.list.d/hashicorp.list \
  && apt-get update && apt-get install -y terraform
) || true &

( npm install -g azure-functions-core-tools@4 --unsafe-perm true ) || true &

(
  ARCH=$(dpkg --print-architecture); [ "$ARCH" = "amd64" ] && ARCH="x64"
  curl -fsSL "https://github.com/github/copilot-cli/releases/latest/download/copilot-linux-${ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin && chmod +x /usr/local/bin/copilot
) || true &

wait

dotnet tool install --tool-path /usr/local/bin Microsoft.PowerApps.CLI.Tool || true
dotnet tool install --tool-path /usr/local/bin TALXIS.CLI || true
dotnet new install TALXIS.DevKit.Templates.Dataverse || true

exit 0
