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

SMITHY_VERSION="1.75.0"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_DIR="$SCRIPT_DIR/cli"
BASE_URL="https://github.com/smithy-lang/smithy/releases/download/${SMITHY_VERSION}"

# SHA256 hashes for smithy-cli-{platform}.zip — update when bumping SMITHY_VERSION.
declare -A PLATFORM_SHA256=(
  ["darwin-aarch64"]="c6d32931b8eaf5e93c159c870387b4e6d8c97363c8c6bb09061291c93cdc9181"
  ["darwin-x86_64"]="a89ca22c941f92476fef804ac54e4d3886204da1f0554a18abd8c66073b7519e"
  ["linux-aarch64"]="8561f72386464ac8cfb0ccd3e864a59199e134f26d8ebf6bd264eebe1aa58098"
  ["linux-x86_64"]="c8d448426061586a0afb93830151838cb92a73e45d9d3733c3367ca905eb32c1"
  ["windows-x64"]="78c6d20843da5656e099012bde966af1d1591a8b1380dd73e628c0a3ef29f633"
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
