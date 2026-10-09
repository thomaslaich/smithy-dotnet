#!/usr/bin/env bash
#
# Restores the Smithy CLI package the way a consumer does, against the packages just packed.
#
# NSmithy.MSBuild fetches NSmithy.SmithyCli.<rid> with a nested restore, which nothing in-repo
# exercises: in-repo builds use the smithy on PATH. This checks that the nested restore sees
# sources declared only in the project (RestoreSources in Directory.Build.props, invisible to
# NuGet.config), and that `dotnet msbuild -t:RestoreSmithyCli` prefetches the CLI so a later
# build that skips restore works with no reachable source.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PACKAGES="$REPO/artifacts/packages"

if ! compgen -G "$PACKAGES/NSmithy.SmithyCli.*.nupkg" > /dev/null; then
  echo "smoke-test: no NSmithy.SmithyCli packages in $PACKAGES; run \`just pack\` first." >&2
  exit 1
fi

# `pwd -P` because macOS hands out a symlinked /var/folders path (see templates/smoke-test.sh).
WORK="$(cd "$(mktemp -d)" && pwd -P)"
# Set SMOKE_KEEP=1 to keep the project and build output around for inspection.
if [[ -z "${SMOKE_KEEP:-}" ]]; then
  trap 'rm -rf "$WORK"' EXIT
else
  echo "smoke-test: working directory $WORK"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Outside the repo, so the root Directory.Build.props / Directory.Packages.props do not leak in.
SRC="$WORK/src"
mkdir -p "$SRC" "$WORK/empty-feed"
cp -R "$REPO/examples/simplerestjson/contracts" "$SRC/contracts"
rm -rf "$SRC/contracts/obj" "$SRC/contracts/bin"
PROJECT="$SRC/contracts/NSmithy.Examples.SimpleRestJson.Contracts.csproj"

# NuGet.config offers nothing; the packages are only reachable through the project's RestoreSources.
cat > "$SRC/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="empty" value="$WORK/empty-feed" />
  </packageSources>
</configuration>
XML
restore_sources() {
  cat > "$SRC/Directory.Build.props" <<XML
<Project>
  <PropertyGroup>
    <RestoreSources>$1</RestoreSources>
  </PropertyGroup>
</Project>
XML
}

restore_sources "$PACKAGES"
echo "smoke-test: restore"
dotnet restore "$PROJECT" --verbosity quiet

echo "smoke-test: RestoreSmithyCli"
dotnet msbuild "$PROJECT" -t:RestoreSmithyCli -verbosity:minimal -nologo
# The example restores into obj/packages instead of the global packages folder.
if ! compgen -G "$SRC/contracts/obj/packages/nsmithy.smithycli.*/*/tools/smithy-cli/bin/smithy*" > /dev/null; then
  echo "smoke-test: RestoreSmithyCli did not restore the Smithy CLI package" >&2
  exit 1
fi

# No source is reachable now: the build must find the prefetched CLI and not restore again.
restore_sources "$WORK/empty-feed"
echo "smoke-test: build without restore"
dotnet build "$PROJECT" --no-restore --verbosity minimal -nologo
if ! compgen -G "$SRC/contracts/obj/*/*/Smithy/NSmithy.Generated.stamp" > /dev/null; then
  echo "smoke-test: the build did not run Smithy code generation" >&2
  exit 1
fi
echo "smoke-test: ok"
