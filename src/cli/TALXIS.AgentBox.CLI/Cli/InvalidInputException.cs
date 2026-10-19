namespace TALXIS.AgentBox.CLI.Cli;

/// <summary>The caller asked for something that can't be done as asked: exit code 2.</summary>
public sealed class InvalidInputException(string message) : ArgumentException(message);
