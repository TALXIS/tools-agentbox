# Contributing to agentbox

agentbox follows the conventions of the TALXIS CLI ([TALXIS/tools-cli](https://github.com/TALXIS/tools-cli),
`CONTRIBUTING.md` and `docs/output-contract.md`), so the two read like one vendor:
- noun groups with aliases (`prerequisite`/`prereq`);
- a leaf command per verb;
- `-f/--format json|text` with TTY auto-detection;
- results on stdout, logs on stderr;
- a safety attribute (`[CliReadOnly]`, `[CliIdempotent]` or `[CliDestructive]`) on every leaf.

A test in `CommandTests` enforces the last two.

## Differences from txc

- **Verbs:** `install`, `update` and `configure` are added to txc's verb list. `install` has no
  `uninstall`, because agentbox sets up environments and doesn't tear them down.
- **Exit code 3** means "the machine is unhealthy" (`prerequisite validate` found something missing
  or broken). Codes 0, 1 and 2 mean what they mean in txc.
- **`install`, `update` and `harness configure` report failures with exit code 0 unless `--strict`.**
  They run in container start hooks, which must not fail.

## Copied from tools-cli

These files were copied from TALXIS/tools-cli at `d70a60e`, not shared as a package, and trimmed;
each file's header says what was kept and what was removed:
- `Core/Cli/AgentBoxLeafCommand.cs` ← `TxcLeafCommand.cs`
- `Core/Cli/Output.cs` ← `OutputFormat.cs`, `OutputContext.cs`, `OutputWriter.cs`,
  `OutputFormatter.cs`, `TxcOutputJsonOptions.cs`
- `Core/Cli/Safety.cs` ← the safety attributes and `IDestructiveCommand.cs`
- `Core/Logging/AgentBoxLoggerFactory.cs` ← `TxcLoggerFactory.cs`, `TxcConsoleFormatter.cs`, the
  JSON stderr logger
- `BannedSymbols.txt`

## Adding or removing a prerequisite

1. Add (or remove) its Feature in
   `src/container/templates/power-platform/.devcontainer/devcontainer.features.json`. The image,
   Claude Code cloud and Copilot use that list.
2. Add (or remove) its entry in `TALXIS.AgentBox.CLI.Core/Prerequisites/PrerequisiteCatalog.cs`.

`CatalogTests` fails when the two disagree.

## Build and test

```bash
cd src/cli
dotnet test
build/write-feature-lock.sh   # optional locally: embeds the Feature lockfile that Linux installs need
dotnet run --project TALXIS.AgentBox.CLI -- prerequisite validate
```

Releases build the lockfile and publish the package to nuget.org (`.github/workflows/publish.yaml`).
