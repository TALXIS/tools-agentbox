using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.DevKit.AgentBox.CLI.Cli;

namespace TALXIS.DevKit.AgentBox.CLI.Auth;

[CliCommand(
    Name = "auth",
    Description = "Authenticate a local user and run the local token sidecar.",
    Children = new[] { typeof(AadCliCommand), typeof(SidecarCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public sealed class AuthCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}

[CliCommand(
    Name = "aad",
    Aliases = ["entra"],
    Description = "Sign in to Microsoft Entra ID.",
    Children = new[] { typeof(AadLoginCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public sealed class AadCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}

[CliCommand(Name = "login", Description = "Sign in by device code and set the one active AgentBox account.")]
public sealed class AadLoginCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("auth aad login");

    [CliOption(Name = "--tenantId", Aliases = ["--tenant-id"], Description = "Tenant ID or domain (default: organizations).", Required = false)]
    public string? TenantId { get; set; }

    [CliOption(Name = "--resource", Description = "Resource URI or GUID to consent to (default: Azure Resource Manager).", Required = false)]
    public string? Resource { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var result = await AgentBoxServices.Current.Auth(Logger).LoginAsync(TenantId, Resource, Cancellation).ConfigureAwait(false);
        OutputFormatter.Write(
            new LoginResult(result.TenantId, result.Account?.Username ?? ""),
            value =>
            {
                OutputWriter.WriteLine("Authentication successful.");
                OutputWriter.WriteLine($"Tenant: {value.Tenant}");
                OutputWriter.WriteLine($"User: {value.User}");
            });
        return ExitSuccess;
    }

    private sealed record LoginResult(string? Tenant, string User);
}

[CliCommand(
    Name = "sidecar",
    Description = "Run the local Managed Identity-compatible token endpoint.",
    Children = new[] { typeof(SidecarStartCommand), typeof(SidecarStopCommand), typeof(SidecarStatusCommand), typeof(SidecarRunCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public sealed class SidecarCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}

[CliCommand(Name = "start", Description = "Start the token sidecar in the background.")]
public sealed class SidecarStartCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("auth sidecar start");

    protected override async Task<int> ExecuteAsync()
    {
        var auth = AgentBoxServices.Current.Auth(Logger);
        if (auth.AccountStore.Read() is null)
            Logger.LogWarning("No authenticated account found. Run: agentbox auth aad login");

        var result = await new SidecarLifecycle(AgentBoxServices.Current.Host, auth, Logger).StartAsync(Cancellation).ConfigureAwait(false);
        var response = new SidecarStartResult(result.Started ? "started" : "already-running", result.Pid, AadAuth.Port);
        OutputFormatter.Write(response, value =>
            OutputWriter.WriteLine(value.Status == "started" ? $"Sidecar started. PID: {value.Pid}" : "Sidecar is already running."));
        return ExitSuccess;
    }

    private sealed record SidecarStartResult(string Status, int? Pid, int Port);
}

[CliCommand(Name = "stop", Description = "Stop the local token sidecar.")]
public sealed class SidecarStopCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("auth sidecar stop");

    protected override async Task<int> ExecuteAsync()
    {
        var stopped = await new SidecarLifecycle(
            AgentBoxServices.Current.Host,
            AgentBoxServices.Current.Auth(Logger),
            Logger).StopAsync(Cancellation).ConfigureAwait(false);
        OutputFormatter.Write(new { Status = stopped ? "stopped" : "not-running" }, value =>
            OutputWriter.WriteLine(value.Status == "stopped" ? "Sidecar stopped." : "Sidecar is not running."));
        return ExitSuccess;
    }
}

[CliCommand(Name = "status", Description = "Show whether the local token sidecar is running and authenticated.")]
public sealed class SidecarStatusCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("auth sidecar status");

    protected override async Task<int> ExecuteAsync()
    {
        var status = await new SidecarLifecycle(
            AgentBoxServices.Current.Host,
            AgentBoxServices.Current.Auth(Logger),
            Logger).StatusAsync(Cancellation).ConfigureAwait(false);
        OutputFormatter.Write(status, value =>
        {
            OutputWriter.WriteLine(value.Running ? "Running" : "Not running");
            if (!value.Running)
                return;
            OutputWriter.WriteLine($"PID: {value.Pid}");
            OutputWriter.WriteLine($"Port: {value.Port}");
            OutputWriter.WriteLine($"Authenticated: {(value.Authenticated ? "Yes" : "No")}");
            if (value.Tenant is not null)
                OutputWriter.WriteLine($"Tenant: {value.Tenant}");
            if (value.User is not null)
                OutputWriter.WriteLine($"User: {value.User}");
        });
        return ExitSuccess;
    }
}

[CliCommand(Name = "run", Hidden = true, Description = "Run the sidecar server in the foreground.")]
public sealed class SidecarRunCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("auth sidecar run");

    [CliOption(Name = "--instance-id", Description = "Internal lifecycle identity.", Required = false)]
    public string? InstanceId { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var auth = AgentBoxServices.Current.Auth(Logger);
        var tokenProvider = await auth.CreateTokenProviderAsync(Cancellation).ConfigureAwait(false);
        await new SidecarServer(AgentBoxServices.Current.Host, tokenProvider, Logger)
            .RunAsync(InstanceId, Cancellation).ConfigureAwait(false);
        return ExitSuccess;
    }
}
