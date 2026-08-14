#!/usr/bin/env bash
# Downloads and extracts the Smithy CLI for all supported platforms into
# tools/smithy-cli/{platform}/. The CLI is a self-contained distribution that
# bundles its own JRE, so no separate Java installation is needed when the
# bundled binary is used.
#
# Run this script before packing NSmithy.MSBuild to ensure the bundled
# binaries are up-to-date.
#
# Usage: bash tools/download-smithy-cli.sh
set -euo pipefail

SMITHY_VERSION="1.73.0"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_DIR="$SCRIPT_DIR/smithy-cli"
BASE_URL="https://github.com/smithy-lang/smithy/releases/download/${SMITHY_VERSION}"

# SHA256 hashes for smithy-cli-{platform}.zip — update when bumping SMITHY_VERSION.
declare -A PLATFORM_SHA256=(
  ["darwin-aarch64"]="daf789553a20822138bc90b913233374613e1a4515a61358241d5c5489be0be9"
  ["darwin-x86_64"]="eb6f7e72245ecf0e3df992314c80dde080e4215716214874a0c3b94f9813562f"
  ["linux-aarch64"]="f69295411846274b9e8128f31ffa1d7ad02fa078047e2c4e46d5d85bcba4fc20"
  ["linux-x86_64"]="9071a7db052da81ab6f4be1b4d43ea152b44b78217be0dd21d37d9ea5ec1942d"
  ["windows-x64"]="32e00abc06f6d1ac9201d8f574bd7a2d62d65eaeb2ea16b3877be18d8febafc2"
)

all_platforms=(
  "darwin-aarch64"
  "darwin-x86_64"
  "linux-aarch64"
  "linux-x86_64"
  "windows-x64"
)

echo "Downloading Smithy CLI ${SMITHY_VERSION} (self-contained, includes JRE)..."

for platform in "${all_platforms[@]}"; do
  dest="$CLI_DIR/$platform"

  # Skip if already downloaded (check for the launcher script / batch file)
  if [[ -f "$dest/bin/smithy" || -f "$dest/bin/smithy.bat" ]]; then
    echo "  [$platform] already present, skipping"
    continue
  fi

  mkdir -p "$dest"

  zip_url="${BASE_URL}/smithy-cli-${platform}.zip"
  zip_file="${dest}/smithy-cli.zip"

  expected_hash="${PLATFORM_SHA256[$platform]}"

  echo "  [$platform] $zip_url"
  if ! curl --fail --silent --show-error --location -o "$zip_file" "$zip_url"; then
    echo "  [$platform] download failed, skipping"
    rm -f "$zip_file"
    rmdir "$dest" 2>/dev/null || true
    continue
  fi

  if command -v sha256sum &>/dev/null; then
    actual_hash=$(sha256sum "$zip_file" | awk '{print $1}')
  else
    actual_hash=$(shasum -a 256 "$zip_file" | awk '{print $1}')
  fi

  if [[ "$actual_hash" != "$expected_hash" ]]; then
    echo "  [$platform] SHA256 mismatch! expected=$expected_hash actual=$actual_hash" >&2
    rm -f "$zip_file"
    rm -rf "$dest"
    exit 1
  fi

  echo "  [$platform] checksum OK"

  tmp_dir=$(mktemp -d)
  unzip -q "$zip_file" -d "$tmp_dir"

  # The ZIP contains smithy-cli-{platform}/ at the top level; move its
  # contents directly into $dest so the launcher is at $dest/bin/smithy.
  inner="$tmp_dir/smithy-cli-${platform}"
  if [[ -d "$inner" ]]; then
    cp -R "$inner/." "$dest/"
  else
    echo "ERROR: expected smithy-cli-${platform}/ inside archive" >&2
    ls "$tmp_dir" >&2
    rm -rf "$tmp_dir" "$zip_file"
    exit 1
  fi

  rm -rf "$tmp_dir" "$zip_file"

  if [[ -f "$dest/bin/smithy" ]]; then
    chmod +x "$dest/bin/smithy"
    echo "    -> $dest/bin/smithy"
  elif [[ -f "$dest/bin/smithy.bat" ]]; then
    echo "    -> $dest/bin/smithy.bat"
  else
    echo "WARNING: launcher not found in $dest/bin/" >&2
  fi
done

echo "Done."
