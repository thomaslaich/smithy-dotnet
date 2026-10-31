#!/usr/bin/env bash
# Downloads and extracts the Smithy CLI for all supported platforms into
# cli/{platform}/. The CLI is a self-contained distribution that bundles its
# own JRE, so no separate Java installation is needed when it is used.
#
# Run this script before packing NSmithy.SmithyCli. SMITHY_VERSION is also the
# version of that package.
#
# Usage: bash packages/NSmithy.SmithyCli/download-smithy-cli.sh
set -euo pipefail

SMITHY_VERSION="1.74.1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_DIR="$SCRIPT_DIR/cli"
BASE_URL="https://github.com/smithy-lang/smithy/releases/download/${SMITHY_VERSION}"

# SHA256 hashes for smithy-cli-{platform}.zip — update when bumping SMITHY_VERSION.
declare -A PLATFORM_SHA256=(
  ["darwin-aarch64"]="ebb5e931936aa503055b4620c4981319f2b060656482553ada0151235ff0f1ac"
  ["darwin-x86_64"]="06b5fa6660fd3ab0281511af1d04f3128e10fa6220c552bc2b94ff18d660886d"
  ["linux-aarch64"]="c563b5387c66fe04b8c459178dfcc08316798bc107c03fd92474dd8a0c028850"
  ["linux-x86_64"]="6b50c0b9f2443400bb7115b9badb54b3609816ec5fb4f625954eadd2f6b2f52a"
  ["windows-x64"]="b6c4c7b83f5520b8872f24248ef1c96dac4a78f0fa92a6f9b1bdb4636166595f"
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
