
# Power Platform CLI (pac) (pac-cli)

Installs the Microsoft Power Platform CLI (pac). When version is 'latest', auto-updates on every container start via postStartCommand.

## Example Usage

```json
"features": {
    "ghcr.io/TALXIS/tools-agentbox/pac-cli:1": {}
}
```

## Options

| Options Id | Description | Type | Default Value |
|-----|-----|-----|-----|
| version | Version of the Power Platform CLI to install. Use 'latest' for auto-updates on each start. | string | latest |

## Customizations

### VS Code Extensions

- `microsoft-IsvExpTools.powerplatform-vscode`



---

_Note: This file was auto-generated from the [devcontainer-feature.json](https://github.com/TALXIS/tools-agentbox/blob/main/src/features/pac-cli/devcontainer-feature.json).  Add additional notes to a `NOTES.md`._
