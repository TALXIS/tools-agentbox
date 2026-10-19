using DotMake.CommandLine;

namespace TALXIS.AgentBox.CLI;

public static class Program
{
    public static Task<int> Main(string[] args) =>
        DotMake.CommandLine.Cli.RunAsync<AgentBoxCliCommand>(args, new CliSettings { EnableDefaultExceptionHandler = true });
}
