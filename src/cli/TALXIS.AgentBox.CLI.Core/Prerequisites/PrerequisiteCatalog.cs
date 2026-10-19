namespace TALXIS.AgentBox.CLI.Core.Prerequisites;

/// <summary>
/// Everything AgentBox provides, in dependency order (a tool's prerequisites come before it).
/// Mirrors the Feature list in src/container/templates/power-platform/.devcontainer/devcontainer.features.json.
/// </summary>
public static class PrerequisiteCatalog
{
    public const string DevkitTemplatesPackage = "TALXIS.DevKit.Templates.Dataverse";

    public static IReadOnlyList<Prerequisite> All { get; } =
    [
        new()
        {
            Id = "git", Description = "Git", Command = "git", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/git",
            Hints = new(Apt: "sudo apt-get install -y git", Winget: "winget install --id Git.Git -e", Brew: "brew install git"),
        },
        new()
        {
            Id = "dotnet", Description = ".NET SDK 10", Command = "dotnet", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/dotnet",
            Hints = new(
                Apt: "curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0",
                Winget: "winget install --id Microsoft.DotNet.SDK.10 -e",
                Brew: "brew install --cask dotnet-sdk"),
        },
        new()
        {
            Id = "node", Description = "Node.js 22", Command = "node", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/node",
            Hints = new(
                Apt: "curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash - && sudo apt-get install -y nodejs",
                Winget: "winget install --id OpenJS.NodeJS.LTS -e",
                Brew: "brew install node@22"),
        },
        new()
        {
            Id = "az", Description = "Azure CLI (with the azure-devops extension)", Command = "az", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/azure-cli",
            Hints = new(
                Apt: "curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash",
                Winget: "winget install --id Microsoft.AzureCLI -e",
                Brew: "brew install azure-cli"),
        },
        new()
        {
            Id = "pwsh", Description = "PowerShell", Command = "pwsh", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/powershell",
            Hints = new(
                Apt: "sudo snap install powershell --classic",
                Winget: "winget install --id Microsoft.PowerShell -e",
                Brew: "brew install --cask powershell"),
        },
        new()
        {
            Id = "terraform", Description = "Terraform", Command = "terraform", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/terraform",
            Hints = new(
                Apt: "see https://developer.hashicorp.com/terraform/install#linux",
                Winget: "winget install --id Hashicorp.Terraform -e",
                Brew: "brew install hashicorp/tap/terraform"),
        },
        new()
        {
            Id = "gh", Description = "GitHub CLI", Command = "gh", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/github-cli",
            Hints = new(
                Apt: "see https://github.com/cli/cli/blob/trunk/docs/install_linux.md",
                Winget: "winget install --id GitHub.cli -e",
                Brew: "brew install gh"),
        },
        new()
        {
            Id = "func", Description = "Azure Functions Core Tools", Command = "func", Kind = PrerequisiteKind.System,
            Feature = "jlaundry/devcontainer-features/azure-functions-core-tools",
            Hints = new(
                Apt: "see https://learn.microsoft.com/azure/azure-functions/functions-run-local",
                Winget: "winget install --id Microsoft.Azure.FunctionsCoreTools -e",
                Brew: "brew tap azure/functions && brew install azure-functions-core-tools@4"),
        },
        new()
        {
            Id = "copilot", Description = "GitHub Copilot CLI", Command = "copilot", Kind = PrerequisiteKind.SelfUpdating,
            Feature = "devcontainers/features/copilot-cli",
            Hints = new(Apt: "npm install -g @github/copilot", Winget: "npm install -g @github/copilot", Brew: "npm install -g @github/copilot"),
        },
        new()
        {
            Id = "claude", Description = "Claude Code", Command = "claude", Kind = PrerequisiteKind.SelfUpdating,
            Feature = "anthropics/devcontainer-features/claude-code",
            Hints = new(
                Apt: "curl -fsSL https://claude.ai/install.sh | bash",
                Winget: "irm https://claude.ai/install.ps1 | iex",
                Brew: "curl -fsSL https://claude.ai/install.sh | bash"),
        },
        new()
        {
            Id = "pac", Description = "Power Platform CLI", Command = "pac", Kind = PrerequisiteKind.DotnetTool,
            Package = "Microsoft.PowerApps.CLI.Tool", Feature = "talxis/tools-agentbox/pac-cli",
            LinuxAutoUpdateMarker = "/etc/devcontainer-pac-cli/auto-update",
            VersionArguments = ["help"],
        },
        new()
        {
            Id = "txc", Description = "TALXIS DevKit CLI", Command = "txc", Kind = PrerequisiteKind.DotnetTool,
            Package = "TALXIS.CLI", Feature = "talxis/tools-agentbox/txc-cli",
            LinuxAutoUpdateMarker = "/etc/devcontainer-txc-cli/auto-update",
        },
        new()
        {
            Id = "keyring", Description = "Secret Service keyring (D-Bus + gnome-keyring)", Command = "agentbox-keyring-init",
            Kind = PrerequisiteKind.System, Feature = "talxis/tools-agentbox/keyring", ContainerOnly = true,
        },
        new()
        {
            Id = "pa", Description = "Power Apps CLI", Command = "pa", Kind = PrerequisiteKind.NpmTool,
            Package = "@microsoft/power-apps-cli", Feature = "talxis/tools-agentbox/pa-cli",
            LinuxAutoUpdateMarker = "/etc/devcontainer-pa-cli/auto-update",
        },
        new()
        {
            Id = "devkit-templates", Description = "TALXIS DevKit dotnet new templates", Command = "dotnet",
            Kind = PrerequisiteKind.DotnetTemplate, Package = DevkitTemplatesPackage,
        },
    ];

    public static Prerequisite? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
