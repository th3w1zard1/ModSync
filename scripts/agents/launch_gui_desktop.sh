#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

instruction_file="${repo_root}/mod-builds/TOMLs/KOTOR1_Full.toml"
kotor_dir="${repo_root}/tmp/kotor_template"
mod_dir="${repo_root}/tmp/mod_downloads"
display_value="${DISPLAY:-:1}"
configuration="Debug"
framework="net9.0"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --instruction-file)
      instruction_file="$2"
      shift 2
      ;;
    --kotor-dir)
      kotor_dir="$2"
      shift 2
      ;;
    --mod-dir)
      mod_dir="$2"
      shift 2
      ;;
    --display)
      display_value="$2"
      shift 2
      ;;
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --framework)
      framework="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "$instruction_file" ]]; then
  echo "Instruction file not found: $instruction_file" >&2
  exit 1
fi

if [[ ! -d "$kotor_dir" || ! -d "$mod_dir" ]]; then
  "$repo_root/scripts/agents/create_template_kotor_install.sh" "$kotor_dir" "$mod_dir"
fi

if [[ -n "${HOME:-}" && -d "${HOME}/.dotnet" ]]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-${HOME}/.dotnet}"
  export PATH="$DOTNET_ROOT:$PATH"
fi

export DISPLAY="$display_value"

dotnet build "$repo_root/src/KOTORModSync.GUI/KOTORModSync.csproj" -c "$configuration" -f "$framework" >/dev/null

output_dir="$repo_root/src/KOTORModSync.GUI/bin/${configuration}/${framework}"
"$repo_root/scripts/agents/ensure_linux_holopatcher.sh" "$output_dir" >/dev/null || true

exec dotnet "$output_dir/KOTORModSync.dll" \
  --instructionFile="$instruction_file" \
  --kotorPath="$kotor_dir" \
  --modDirectory="$mod_dir"
