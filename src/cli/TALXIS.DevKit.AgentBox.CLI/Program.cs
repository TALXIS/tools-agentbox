using DotMake.CommandLine;

namespace TALXIS.DevKit.AgentBox.CLI;

public static class Program
{
    public static Task<int> Main(string[] args) =>
        DotMake.CommandLine.Cli.RunAsync<AgentBoxCliCommand>(args, new CliSettings { EnableDefaultExceptionHandler = true });
}
