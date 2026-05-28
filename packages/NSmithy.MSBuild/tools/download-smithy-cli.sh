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

# SHA256 hashes for smithy-cli-{platform}.zip — update when bumping SMITHY_VERSION.
declare -A PLATFORM_SHA256=(
  ["darwin-aarch64"]="e836bb468eb117f05597fa263864681728950e32c10a85592eb4dd643cfdee88"
  ["darwin-x86_64"]="55b5e397fd42fea407326e512daf9bcd38819c03534e1243e4a0fc71a9ec5ded"
  ["linux-aarch64"]="2bbed6177b0c4fc2f75c4266a5cf72571ca35ce66da8800a90d4cf03c6bb2d42"
  ["linux-x86_64"]="ee6e6d24416b53624ba7f323628b2ca8aa67a349fbe3b2e92e98172c3f3d6a45"
  ["windows-x64"]="604f7017f4dfc50b802fa8d74401bbb4604ccbec5af2df39639897f467aaf663"
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
