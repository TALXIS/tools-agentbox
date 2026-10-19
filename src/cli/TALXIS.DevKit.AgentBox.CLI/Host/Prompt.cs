namespace TALXIS.DevKit.AgentBox.CLI.Host;

/// <summary>Asks the person at the terminal. Only used when <see cref="IHostInfo.IsInteractive"/>.</summary>
public interface IPrompt
{
    string? Ask(string question);
}

public sealed class ConsolePrompt : IPrompt
{
#pragma warning disable RS0030 // The one approved interactive prompt; the question goes to stderr, not the result stream
    public string? Ask(string question)
    {
        Console.Error.Write(question + " ");
        return Console.ReadLine();
    }
#pragma warning restore RS0030
}
