namespace TALXIS.AgentBox.CLI.Prerequisites;

/// <summary>
/// Everything AgentBox provides, in install order. Each Feature here is also listed in
/// src/container/templates/power-platform/.devcontainer/devcontainer.features.json; a test keeps them in step.
/// </summary>
public static class PrerequisiteCatalog
{
    public static IReadOnlyList<Prerequisite> All { get; } =
    [
        new()
        {
            Id = "git", Description = "Git", Command = "git", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/git",
            Manual = new("sudo apt-get install -y git", "winget install --id Git.Git -e", "brew install git"),
        },
        new()
        {
            Id = "dotnet", Description = ".NET SDK 10", Command = "dotnet", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/dotnet",
            RequiredVersion = new(["--list-sdks"], @"^10\."),
            Manual = new(
                "curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0",
                "winget install --id Microsoft.DotNet.SDK.10 -e",
                "brew install --cask dotnet-sdk"),
        },
        new()
        {
            Id = "node", Description = "Node.js 22", Command = "node", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/node",
            Manual = new(
                "curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash - && sudo apt-get install -y nodejs",
                "winget install --id OpenJS.NodeJS.22 -e",
                "brew install node@22"),
        },
        new()
        {
            Id = "az", Description = "Azure CLI", Command = "az", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/azure-cli",
            Manual = new(
                "curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash",
                "winget install --id Microsoft.AzureCLI -e",
                "brew install azure-cli"),
        },
        new()
        {
            Id = "pwsh", Description = "PowerShell", Command = "pwsh", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/powershell",
            Manual = new("sudo snap install powershell --classic", "winget install --id Microsoft.PowerShell -e", "brew install --cask powershell"),
        },
        new()
        {
            Id = "terraform", Description = "Terraform", Command = "terraform", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/terraform",
            Manual = new(
                "https://developer.hashicorp.com/terraform/install#linux",
                "winget install --id Hashicorp.Terraform -e",
                "brew install hashicorp/tap/terraform"),
        },
        new()
        {
            Id = "gh", Description = "GitHub CLI", Command = "gh", Kind = PrerequisiteKind.System,
            Feature = "devcontainers/features/github-cli",
            Manual = new("https://github.com/cli/cli/blob/trunk/docs/install_linux.md", "winget install --id GitHub.cli -e", "brew install gh"),
        },
        new()
        {
            Id = "func", Description = "Azure Functions Core Tools", Command = "func", Kind = PrerequisiteKind.System,
            Feature = "jlaundry/devcontainer-features/azure-functions-core-tools",
            Manual = new(
                "https://learn.microsoft.com/azure/azure-functions/functions-run-local",
                "winget install --id Microsoft.Azure.FunctionsCoreTools -e",
                "brew tap azure/functions && brew install azure-functions-core-tools@4"),
        },
        new()
        {
            Id = "copilot", Description = "GitHub Copilot CLI", Command = "copilot", Kind = PrerequisiteKind.SelfManaged,
            Feature = "devcontainers/features/copilot-cli",
            Manual = ManualInstall.Everywhere("npm install -g @github/copilot"),
        },
        new()
        {
            Id = "claude", Description = "Claude Code", Command = "claude", Kind = PrerequisiteKind.SelfManaged,
            Feature = "anthropics/devcontainer-features/claude-code",
            Manual = new("curl -fsSL https://claude.ai/install.sh | bash", "irm https://claude.ai/install.ps1 | iex", "curl -fsSL https://claude.ai/install.sh | bash"),
        },
        new()
        {
            Id = "pac", Description = "Power Platform CLI", Command = "pac", Kind = PrerequisiteKind.DotnetTool,
            Package = "Microsoft.PowerApps.CLI.Tool", Feature = "talxis/tools-agentbox/pac-cli",
        },
        new()
        {
            Id = "txc", Description = "TALXIS DevKit CLI", Command = "txc", Kind = PrerequisiteKind.DotnetTool,
            Package = "TALXIS.CLI", Feature = "talxis/tools-agentbox/txc-cli",
        },
        new()
        {
            Id = "keyring", Description = "Secret Service keyring", Command = "agentbox-keyring-init", Kind = PrerequisiteKind.System,
            Feature = "talxis/tools-agentbox/keyring", ContainerOnly = true,
        },
        new()
        {
            Id = "pa", Description = "Power Apps CLI", Command = "pa", Kind = PrerequisiteKind.NpmTool,
            Package = "@microsoft/power-apps-cli", Feature = "talxis/tools-agentbox/pa-cli", NpmPrefix = "/usr/local/lib/power-apps-cli",
        },
        new()
        {
            // In a container its hooks run it; elsewhere dnx is enough.
            Id = "agentbox", Description = "AgentBox CLI", Command = "agentbox", Kind = PrerequisiteKind.SelfManaged,
            Feature = "talxis/tools-agentbox/agentbox", ContainerOnly = true,
        },
        new()
        {
            Id = "devkit-templates", Description = "TALXIS DevKit templates", Command = "dotnet", Kind = PrerequisiteKind.DotnetTemplate,
            Package = "TALXIS.DevKit.Templates.Dataverse",
        },
    ];

    /// <summary>The named prerequisites in catalog order, or all of them. Unknown ids are input errors.</summary>
    public static IReadOnlyList<Prerequisite> Select(IReadOnlyCollection<string>? ids)
    {
        if (ids is not { Count: > 0 })
            return All;
        var unknown = ids.Where(id => !All.Any(p => Matches(p, id))).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown prerequisite(s): {string.Join(", ", unknown)}. See 'agentbox prerequisite list'.");
        return All.Where(p => ids.Any(id => Matches(p, id))).ToList();
    }

    private static bool Matches(Prerequisite p, string id) => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase);
}
