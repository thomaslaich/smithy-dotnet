#!/usr/bin/env bash
#
# Builds a small NSmithy project against the packed packages once per way of providing the
# Smithy CLI: the NSmithy.SmithyCli package, smithy on PATH, SmithyCliPath, and the version check
# with older and newer CLIs, turned off, and finding no CLI at all. Also checks where Maven dependencies
# resolve from, with and without the bundled repository.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PACKAGES="$REPO/artifacts/packages"
LOCAL="0.0.0-SNAPSHOT"
SMITHY_VERSION="$(sed -n 's/^SMITHY_VERSION="\(.*\)"$/\1/p' "$REPO/packages/NSmithy.SmithyCli/download-smithy-cli.sh")"

for package in "NSmithy.MSBuild.$LOCAL" "NSmithy.SmithyCli.$SMITHY_VERSION" "dotnet-nsmithy.$LOCAL"; do
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

# CLIs that report another version and otherwise run the real one.
fake_cli() { # fake_cli <dir> <version>
  mkdir -p "$WORK/$1"
  cat > "$WORK/$1/smithy" <<SH
#!/usr/bin/env bash
if [[ "\${1:-}" == "--version" ]]; then echo "$2"; exit 0; fi
exec "$REAL_SMITHY" "\$@"
SH
  chmod +x "$WORK/$1/smithy"
}
fake_cli old-cli 1.71.0
fake_cli new-cli 99.0.0

# PATH without any smithy, for the missing-CLI case.
NO_SMITHY_PATH=""
IFS=: read -ra path_entries <<< "$PATH"
for entry in "${path_entries[@]}"; do
  [[ -x "$entry/smithy" ]] || NO_SMITHY_PATH="$NO_SMITHY_PATH${NO_SMITHY_PATH:+:}$entry"
done

# project <name> <with-package> [maven-dependency]
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
  local maven=""
  if [[ -n "${3:-}" ]]; then
    maven=", \"maven\": { \"dependencies\": [\"$3\"] }"
  fi
  cat > "$dir/smithy-build.json" <<JSON
{
  "version": "1.0",
  "sources": ["model"]$maven
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
# A dependency NSmithy.MSBuild does not bundle.
unbundled="$(project unbundled yes software.amazon.smithy:smithy-waiters:$SMITHY_VERSION)"

echo "== Smithy CLI resolution =="
# The package wins over PATH, and its CLI is the one that runs.
expect package "$with_package" success "nsmithy.smithycli/$SMITHY_VERSION" "$PATH"
expect path "$without_package" success "" "$PATH"
expect cli-path "$without_package" success "$WORK/old-cli/smithy" "$NO_SMITHY_PATH" \
  -p:SmithyCliPath="$WORK/old-cli/smithy" -p:SmithyCliVersionCheck=none
expect cli-path-over-package "$with_package" NSMITHYCLIVERSION "" "$PATH" \
  -p:SmithyCliPath="$WORK/old-cli/smithy"
expect version-mismatch "$without_package" NSMITHYCLIVERSION "" "$NO_SMITHY_PATH" \
  -p:SmithyCliPath="$WORK/old-cli/smithy"
expect missing "$without_package" NSMITHYCLIMISSING "" "$NO_SMITHY_PATH"
# A newer CLI satisfies the default minimum check but not an exact one.
expect newer-cli "$without_package" success "" "$NO_SMITHY_PATH" \
  -p:SmithyCliPath="$WORK/new-cli/smithy"
expect newer-cli-exact "$without_package" NSMITHYCLIVERSION "" "$NO_SMITHY_PATH" \
  -p:SmithyCliPath="$WORK/new-cli/smithy" -p:SmithyCliVersionCheck=exact
# The package must match exactly unless the project relaxes the check: an older SmithyVersion
# stands in for a package that is newer than the NSmithy release.
expect package-newer "$with_package" NSMITHYCLIVERSION "" "$NO_SMITHY_PATH" \
  -p:SmithyVersion=1.72.0
expect package-newer-minimum "$with_package" success "" "$NO_SMITHY_PATH" \
  -p:SmithyVersion=1.72.0 -p:SmithyCliVersionCheck=minimum

echo "== Maven resolution =="
# With the bundled repo, the CLI does not assume Maven Central: an unbundled dependency needs a
# repository in the build file. Without it, the build file's maven block (Maven Central) applies.
expect unbundled-dependency "$unbundled" NSMITHYCLI "" "$PATH"
expect unbundled-dependency-without-bundled-repo "$unbundled" success "" "$PATH" \
  -p:SmithyUseBundledMavenRepo=false

echo "== dotnet nsmithy smithy =="
# The tool runs the CLI the project resolves, so with smithy off PATH only the package can answer.
(cd "$WORK" && dotnet tool install dotnet-nsmithy --tool-path "$WORK/tools" --version "$LOCAL" \
  --add-source "$PACKAGES" > "$WORK/tool-install.log" 2>&1)
# tool <name> <dir> <expected output pattern> <PATH> <args...>
tool() {
  local name="$1" dir="$2" pattern="$3" path="$4"
  shift 4
  if (cd "$dir" && PATH="$path" "$WORK/tools/dotnet-nsmithy" smithy -- "$@") > "$WORK/$name.log" 2>&1 \
    && grep -q -- "$pattern" "$WORK/$name.log"; then
    pass "$name"
  else
    fail "$name" "expected '$pattern'"
  fi
}
tool tool-package-version "$with_package" "^$SMITHY_VERSION" "$NO_SMITHY_PATH" --version
tool tool-package-validate "$with_package" "" "$NO_SMITHY_PATH" validate
tool tool-path-version "$without_package" "^$SMITHY_VERSION" "$PATH" --version

if ((${#failed[@]})); then
  echo "smoke-test: ${#failed[@]} failed: ${failed[*]}" >&2
  exit 1
fi
echo "smoke-test: all passed"
