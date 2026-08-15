#!/usr/bin/env bash
#
# Scaffolds every `dotnet new` template combination and builds it, the way a user does.
#
# The templates are the only consumer surface nothing else exercises: they carry a hand-written
# Program.cs against generated APIs, so a codegen rename compiles everywhere in-repo and silently
# breaks `dotnet new` until someone runs the quick start. This runs against the packages just
# packed from the working tree (not the released ones the templates pin), so drift fails in the
# pull request that causes it rather than after the next release.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGES="$REPO/artifacts/packages"
# Version the template content was packed with; rewritten to the locally packed dev version below.
PINNED="$(tr -d '[:space:]' < "$REPO/VERSION")"
LOCAL="0.0.0-SNAPSHOT"

if [[ ! -f "$PACKAGES/NSmithy.Templates.$LOCAL.nupkg" ]]; then
  echo "smoke-test: $PACKAGES/NSmithy.Templates.$LOCAL.nupkg not found — run \`just pack\` first." >&2
  exit 1
fi

# `pwd -P` because macOS hands out a symlinked /var/folders path, and NSmithy.MSBuild then
# generates sources under the resolved path while MSBuild globs them under the symlinked one,
# so codegen silently contributes nothing.
WORK="$(cd "$(mktemp -d)" && pwd -P)"
# Set SMOKE_KEEP=1 to keep the scaffolded projects and build logs around for inspection.
if [[ -z "${SMOKE_KEEP:-}" ]]; then
  trap 'rm -rf "$WORK"' EXIT
else
  echo "smoke-test: working directory $WORK"
fi

# Keep the template store out of the developer's real one: `dotnet new install` is global state,
# and this installs a SNAPSHOT build that would shadow whatever they have installed.
export DOTNET_CLI_HOME="$WORK/cli-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
mkdir -p "$DOTNET_CLI_HOME"

# Outside the repo, so the root Directory.Build.props / Directory.Packages.props do not leak in.
SRC="$WORK/src"
mkdir -p "$SRC"
cat > "$SRC/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$PACKAGES" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

dotnet new install "$PACKAGES/NSmithy.Templates.$LOCAL.nupkg" > /dev/null

# Repoint the scaffolded project at the locally packed packages and codegen plugin.
localize() {
  find "$1" \( -name '*.csproj' -o -name 'smithy-build.json' \) -type f -print0 |
    while IFS= read -r -d '' f; do
      sed -i.bak "s/$PINNED/$LOCAL/g" "$f" && rm -f "$f.bak"
    done
}

failed=()
build() { # build <label> <project-dir>
  if dotnet build "$2" --verbosity quiet --nologo > "$WORK/$1.log" 2>&1; then
    echo "  PASS  $1"
  else
    echo "  FAIL  $1"
    tail -n 30 "$WORK/$1.log"
    failed+=("$1")
  fi
}

for protocol in restJson1 simpleRestJson rpcv2Cbor grpc; do
  echo "== $protocol =="

  # Quick start: contracts owns the model, server and client consume it by project reference.
  qs="$SRC/qs-$protocol"
  mkdir -p "$qs"
  cp "$SRC/NuGet.config" "$qs/"
  dotnet new nsmithy-contracts -o "$qs/Hello.Contracts" -n Hello.Contracts --protocol "$protocol" > /dev/null
  dotnet new nsmithy-server -o "$qs/Hello.Server" -n Hello.Server --protocol "$protocol" \
    --contracts Hello.Contracts --with-docs > /dev/null
  dotnet new nsmithy-client -o "$qs/Hello.Client" -n Hello.Client --protocol "$protocol" > /dev/null
  # What the quick start tells users to do for local development: drop the Maven contracts
  # reference so NSmithy synthesizes a build from the sibling contracts project instead.
  rm -f "$qs/Hello.Client/smithy-build.json"
  dotnet add "$qs/Hello.Client/Hello.Client.csproj" reference \
    "$qs/Hello.Contracts/Hello.Contracts.csproj" > /dev/null
  localize "$qs"
  build "$protocol-contracts" "$qs/Hello.Contracts"
  build "$protocol-server" "$qs/Hello.Server"
  build "$protocol-client" "$qs/Hello.Client"

  # Standalone server: owns model/ and smithy-build.json itself, and no docs endpoints.
  solo="$SRC/solo-$protocol"
  mkdir -p "$solo"
  cp "$SRC/NuGet.config" "$solo/"
  dotnet new nsmithy-server -o "$solo/Hello.Server" -n Hello.Server --protocol "$protocol" > /dev/null
  localize "$solo"
  build "$protocol-server-standalone" "$solo/Hello.Server"
done

if (( ${#failed[@]} > 0 )); then
  echo "smoke-test: ${#failed[@]} template build(s) failed: ${failed[*]}" >&2
  exit 1
fi
echo "smoke-test: all template combinations built."
