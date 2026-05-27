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

SMITHY_VERSION="1.68.0"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_DIR="$SCRIPT_DIR/smithy-cli"
BASE_URL="https://github.com/smithy-lang/smithy/releases/download/${SMITHY_VERSION}"

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

  echo "  [$platform] $zip_url"
  if ! curl --fail --silent --show-error --location -o "$zip_file" "$zip_url"; then
    echo "  [$platform] download failed, skipping"
    rm -f "$zip_file"
    rmdir "$dest" 2>/dev/null || true
    continue
  fi

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
