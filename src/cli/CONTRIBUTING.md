# Contributing to agentbox

agentbox follows the TALXIS CLI's conventions ([TALXIS/tools-cli](https://github.com/TALXIS/tools-cli)),
so the two feel like one product: noun groups with verb commands, `--format json|text`, results on
stdout and logs on stderr, the same exit codes plus 3 for "not ready".

A new prerequisite is a Feature in
`src/container/templates/power-platform/.devcontainer/devcontainer.features.json` plus an entry in
`PrerequisiteCatalog`; a test fails until both exist.

```bash
dotnet test src/cli
src/cli/build/pack.sh 0.0.0-local out    # pins the Features and packs, as releases do
```

CI sets `AGENTBOX_FEATURES_FROM` to `src/container/features` so agentbox installs this repo's
Features from the checkout instead of the pinned, published ones; a pull request's Feature changes
are then what its tests install. A new Feature still has to be published once before the tool list
can name it.
