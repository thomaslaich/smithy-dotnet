#!/usr/bin/env bash
#
# Builds a small NSmithy project against the packed packages once per way of providing the
# Smithy CLI: the NSmithy.SmithyCli package, smithy on PATH, SmithyCliPath, and the version check
# failing, turned off, and finding no CLI at all. Also checks the warning for synthesis-only
# properties set next to a project's own smithy-build.json.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PACKAGES="$REPO/artifacts/packages"
LOCAL="0.0.0-SNAPSHOT"
SMITHY_VERSION="$(sed -n 's/^SMITHY_VERSION="\(.*\)"$/\1/p' "$REPO/packages/NSmithy.SmithyCli/download-smithy-cli.sh")"

for package in "NSmithy.MSBuild.$LOCAL" "NSmithy.SmithyCli.$SMITHY_VERSION"; do
  if [[ ! -f "$PACKAGES/$package.nupkg" ]]; then
    echo "smoke-test: $PACKAGES/$package.nupkg not found; run \`just pack\` first." >&2
    exit 1
  fi
done

REAL_SMITHY="$(command -v smithy || true)"
if [[ -z "$REAL_SMITHY" ]]; then
  echo "smoke-test: needs a Smithy CLI $SMITHY_VERSION on PATH (the devenv shell provides one)." >&2
  exit 1
fi

# `pwd -P`: see templates/smoke-test.sh.
WORK="$(cd "$(mktemp -d)" && pwd -P)"
if [[ -z "${SMOKE_KEEP:-}" ]]; then
  trap 'rm -rf "$WORK"' EXIT
else
  echo "smoke-test: working directory $WORK"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Outside the repo, so the root Directory.Build.props / Directory.Packages.props do not leak in.
# A private packages folder, so the packed NSmithy.SmithyCli is the one restored.
cat > "$WORK/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$WORK/packages" />
  </config>
  <packageSources>
    <clear />
    <add key="local" value="$PACKAGES" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

# A CLI that reports another version and otherwise runs the real one.
mkdir -p "$WORK/old-cli"
cat > "$WORK/old-cli/smithy" <<SH
#!/usr/bin/env bash
if [[ "\${1:-}" == "--version" ]]; then echo "1.71.0"; exit 0; fi
exec "$REAL_SMITHY" "\$@"
SH
chmod +x "$WORK/old-cli/smithy"

# PATH without any smithy, for the missing-CLI case.
NO_SMITHY_PATH=""
IFS=: read -ra path_entries <<< "$PATH"
for entry in "${path_entries[@]}"; do
  [[ -x "$entry/smithy" ]] || NO_SMITHY_PATH="$NO_SMITHY_PATH${NO_SMITHY_PATH:+:}$entry"
done

# project <name> <with-package>
project() {
  local dir="$WORK/$1"
  mkdir -p "$dir/model"
  local cli_reference=""
  if [[ "$2" == "yes" ]]; then
    cli_reference="<PackageReference Include=\"NSmithy.SmithyCli\" Version=\"$SMITHY_VERSION\" />"
  fi
  cat > "$dir/Smoke.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NSmithy.MSBuild" Version="$LOCAL" />
    $cli_reference
  </ItemGroup>
</Project>
XML
  # Contracts-style: the CLI validates the model and builds it, which is all this test needs.
  cat > "$dir/smithy-build.json" <<JSON
{
  "version": "1.0",
  "sources": ["model"]
}
JSON
  cat > "$dir/model/smoke.smithy" <<'SMITHY'
$version: "2"
namespace smoke

service Smoke {
    version: "1"
    operations: [Ping]
}

operation Ping {
    input := {}
    output := {}
}
SMITHY
  dotnet restore "$dir" --verbosity quiet > "$WORK/$1.restore.log" 2>&1
  echo "$dir"
}

failed=()
pass() { echo "  PASS  $1"; }
fail() {
  echo "  FAIL  $1: $2"
  tail -n 20 "$WORK/$1.log"
  failed+=("$1")
}

# expect <name> <dir> <success|error-code> <log pattern or ''> [env PATH] [msbuild args...]
expect() {
  local name="$1" dir="$2" outcome="$3" pattern="$4" path="$5"
  shift 5
  local status=0
  PATH="$path" dotnet build "$dir" --no-restore --no-incremental -v:n -nologo "$@" > "$WORK/$name.log" 2>&1 || status=$?
  if [[ "$outcome" == "success" && $status -ne 0 ]]; then
    fail "$name" "build failed"
  elif [[ "$outcome" != "success" && $status -eq 0 ]]; then
    fail "$name" "build succeeded, expected $outcome"
  elif [[ "$outcome" != "success" ]] && ! grep -q "error $outcome" "$WORK/$name.log"; then
    fail "$name" "expected error $outcome"
  elif [[ -n "$pattern" ]] && ! grep -q -- "$pattern" "$WORK/$name.log"; then
    fail "$name" "log does not contain '$pattern'"
  else
    pass "$name"
  fi
}

with_package="$(project with-package yes)"
without_package="$(project without-package no)"

echo "== Smithy CLI resolution =="
# The package wins over PATH, and its CLI is the one that runs.
expect package "$with_package" success "nsmithy.smithycli/$SMITHY_VERSION" "$PATH"
expect path "$without_package" success "" "$PATH"
expect cli-path "$without_package" success "$WORK/old-cli/smithy" "$NO_SMITHY_PATH" \
  -p:SmithyCliPath="$WORK/old-cli/smithy" -p:SmithyCliVersionCheck=false
expect cli-path-over-package "$with_package" NSMITHYCLIVERSION "" "$PATH" \
  -p:SmithyCliPath="$WORK/old-cli/smithy"
expect version-mismatch "$without_package" NSMITHYCLIVERSION "" "$NO_SMITHY_PATH" \
  -p:SmithyCliPath="$WORK/old-cli/smithy"
expect missing "$without_package" NSMITHYCLIMISSING "" "$NO_SMITHY_PATH"

echo "== Own smithy-build.json =="
# Synthesis-only properties have no effect next to an explicit build file, so they warn.
expect ignored-property "$with_package" success "warning NSMITHYIGNORED" "$PATH" \
  -p:SmithyGenerateFakes=true

if ((${#failed[@]})); then
  echo "smoke-test: ${#failed[@]} failed: ${failed[*]}" >&2
  exit 1
fi
echo "smoke-test: all passed"
