#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output_dir="${1:-$repo_root/src/KOTORModSync.GUI/bin/Debug/net9.0}"
resources_dir="$output_dir/Resources"
vendor_linux_patcher="$repo_root/vendor/bin/HoloPatcher_linux"
target_path="$resources_dir/holopatcher"

mkdir -p "$resources_dir"

if [[ -e "$target_path" ]]; then
  chmod +x "$target_path" || true
  echo "holopatcher already present at $target_path"
  exit 0
fi

if [[ ! -f "$vendor_linux_patcher" ]]; then
  echo "Missing vendor binary: $vendor_linux_patcher" >&2
  exit 1
fi

ln -sf "$vendor_linux_patcher" "$target_path"
chmod +x "$vendor_linux_patcher" "$target_path" || true

echo "Linked Linux HoloPatcher:"
echo "  source=$vendor_linux_patcher"
echo "  target=$target_path"
