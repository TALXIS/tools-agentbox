// Copied from TALXIS/tools-cli @ d70a60e:
//   src/TALXIS.CLI.Core/Shared/CliReadOnlyAttribute.cs, CliIdempotentAttribute.cs,
//   CliDestructiveAttribute.cs, src/TALXIS.CLI.Core/Abstractions/IDestructiveCommand.cs
// Every leaf command declares exactly one safety posture (enforced by CommandConventionTests).
namespace TALXIS.AgentBox.CLI.Core.Cli;

/// <summary>No side effects: a pure read.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliReadOnlyAttribute : Attribute { }

/// <summary>Safe to retry: repeated runs end in the same state.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliIdempotentAttribute : Attribute { }

/// <summary>Irreversible or dangerous; the command must implement <see cref="IDestructiveCommand"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliDestructiveAttribute(string impact) : Attribute
{
    public string Impact { get; } = impact;
}

/// <summary>Exposes <c>--yes</c>; destructive commands refuse to run without it.</summary>
public interface IDestructiveCommand
{
    bool Yes { get; set; }
}
