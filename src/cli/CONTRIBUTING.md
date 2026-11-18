# Contributing to agentbox

agentbox follows the TALXIS CLI's conventions ([TALXIS/tools-cli](https://github.com/TALXIS/tools-cli)),
so the two feel like one product: noun groups with verb commands, `--format json|text`, results on
stdout and logs on stderr, the same exit codes plus 3 for "not ready".

A new prerequisite is a Feature in
`src/container/agentbox-features.json` plus an entry in
`PrerequisiteCatalog`; a test fails until both exist.

```bash
dotnet test src/cli
# Pins the Features and packs, as releases do, in the image Dockerfile's agentbox-cli stage (no
# local jq/npm needed). The nupkg lands in out/.
docker buildx build --file src/container/image/power-platform/Dockerfile --target agentbox-cli \
    --build-arg AGENTBOX_VERSION=0.0.0-local --output type=local,dest=out .
```

To build the full image with the CLI from this checkout and this checkout's Features:

```bash
docker build --file src/container/image/power-platform/Dockerfile \
    --build-context agentbox-features=src/container/features \
    --build-arg AGENTBOX_FEATURES_FROM=/tmp/agentbox-features \
    --tag agentbox-local .
```

The Dockerfile packs the CLI from this checkout as `999.0.0` by default. That version is higher
than the published CLI, so the image installs the local package. If you override
`AGENTBOX_VERSION`, keep it higher than the published version or NuGet may install the published
package instead.

CI sets `AGENTBOX_FEATURES_FROM` (with `--build-context agentbox-features=src/container/features`
and `AGENTBOX_FEATURES_FROM=/tmp/agentbox-features` for the Docker build, and `src/container/features`
for agentbox), so this repo's Features come from the checkout instead of the pinned, published ones.
A pull request's Feature changes, and new Features, are then what its tests install.

After build, you can run locally with: `docker run --rm -it -e MSI_ENDPOINT=http://localhost:50343/metadata/identity/oauth2/token -e MSI_SECRET=agentbox -e DEFAULT_IDENTITY_CLIENT_ID=04b07795-8ddb-461a-bbee-02f9e1bf7b46 agentbox-local bash`

To test with locally built TXC, you can use (or something similar to match the current version):
```powershell
docker run --rm -it `
   -e MSI_ENDPOINT=http://localhost:50343/metadata/identity/oauth2/token `
   -e MSI_SECRET=agentbox `
   -e DEFAULT_IDENTITY_CLIENT_ID=04b07795-8ddb-461a-bbee-02f9e1bf7b46 `
   -v "C:\Users\<user>\Source\repos\TALXIS\tools-cli\src\TALXIS.CLI\bin\Release\net10.0:/usr/local/share/txc/.store/talxis.cli/1.22.2/talxis.cli/1.22.2/tools/net10.0/any" `
   agentbox-local bash
```