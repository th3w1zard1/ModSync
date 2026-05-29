#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
game=""
game_dir=""
source_dir=""
output_path=""
export_format=""
dry_run=false
dry_run_only=false
auto_generate_local=false
validate_only=false
export_all_formats=false
install=false

usage() {
  cat <<'EOF'
Usage: cli_full_build_pipeline.sh --game k1|k2 [options]

Merges mod-builds markdown + TOML (two-source pipeline), optionally exports another
format, then runs structural validate and/or VFS dry-run.

Options:
  --game k1|k2           Which full build (required)
  --game-dir PATH        KOTOR install directory (required for --dry-run / --dry-run-only)
  --source-dir PATH      Mod workspace directory (required for --dry-run / --dry-run-only)
  --output PATH          Merged TOML output path (default: ./tmp/KOTOR{1,2}_Full_merged.toml)
  --export-format FMT    Also write merged set as toml|json|yaml|xml (optional)
  --dry-run              Run validate --dry-run after merge (needs game + source dirs)
  --dry-run-only         Run validate --dry-run-only (skip archive checks; VFS only)
  --auto-generate-local  Fill missing instructions from archives in --source-dir during merge
  --export-all-formats   Write merged set as TOML, JSON, YAML, and XML
  --install              Best-effort install after merge (requires game + source dirs; use with --dry-run-only first)
  --validate-only        Skip merge; validate --input only (uses --output or default merged path)
  -h, --help             Show this help

Examples:
  ./scripts/agents/cli_full_build_pipeline.sh --game k1 \
    --game-dir ./tmp/kotor_template --source-dir ./tmp/mod_downloads --dry-run

  ./scripts/agents/cli_full_build_pipeline.sh --game k2 --export-format json \
    --output ./tmp/KOTOR2_Full_merged.toml
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --game)
      game="$2"
      shift 2
      ;;
    --game-dir)
      game_dir="$2"
      shift 2
      ;;
    --source-dir)
      source_dir="$2"
      shift 2
      ;;
    --output)
      output_path="$2"
      shift 2
      ;;
    --export-format)
      export_format="$2"
      shift 2
      ;;
    --dry-run)
      dry_run=true
      shift
      ;;
    --dry-run-only)
      dry_run_only=true
      shift
      ;;
    --auto-generate-local)
      auto_generate_local=true
      shift
      ;;
    --export-all-formats)
      export_all_formats=true
      shift
      ;;
    --install)
      install=true
      shift
      ;;
    --validate-only)
      validate_only=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "error: unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$game" ]]; then
  echo "error: --game k1|k2 is required" >&2
  usage >&2
  exit 1
fi

case "$game" in
  k1|K1|kotor1)
    md_path="$repo_root/mod-builds/content/k1/full.md"
    toml_path="$repo_root/mod-builds/TOMLs/KOTOR1_Full.toml"
    default_output="$repo_root/tmp/KOTOR1_Full_merged.toml"
    ;;
  k2|K2|kotor2)
    md_path="$repo_root/mod-builds/content/k2/full.md"
    toml_path="$repo_root/mod-builds/TOMLs/KOTOR2_Full.toml"
    default_output="$repo_root/tmp/KOTOR2_Full_merged.toml"
    ;;
  *)
    echo "error: --game must be k1 or k2" >&2
    exit 1
    ;;
esac

if [[ -z "$output_path" ]]; then
  output_path="$default_output"
fi

if [[ ! -f "$md_path" || ! -f "$toml_path" ]]; then
  echo "error: mod-builds sources missing. Clone https://github.com/th3w1zard1/mod-builds to ./mod-builds" >&2
  exit 1
fi

mkdir -p "$(dirname "$output_path")"

run_core() {
  dotnet run --project "$repo_root/src/KOTORModSync.Core/KOTORModSync.Core.csproj" \
    -f net9.0 -- "$@"
}

if [[ "$validate_only" != true ]]; then
  echo "Merging TOML (instructions) + markdown (metadata)..."
  merge_args=(
    merge
    --existing "$toml_path"
    --incoming "$md_path"
    --use-existing-order
    --prefer-existing-instructions
    --prefer-existing-options
    --prefer-existing-modlinks
    -f toml
    -o "$output_path"
  )
  if [[ "$auto_generate_local" == true ]]; then
    if [[ -z "$source_dir" ]]; then
      echo "error: --auto-generate-local requires --source-dir" >&2
      exit 1
    fi
    merge_args+=(--source-path "$source_dir" --auto-generate-local)
  fi
  run_core "${merge_args[@]}"

  if [[ -n "$export_format" ]]; then
    export_path="${output_path%.*}.${export_format,,}"
    echo "Exporting merged set as ${export_format} -> $export_path"
    run_core convert --input "$output_path" -f "$export_format" -o "$export_path"
  fi

  if [[ "$export_all_formats" == true ]]; then
    for fmt in json yaml xml; do
      export_path="${output_path%.*}.${fmt}"
      echo "Exporting merged set as ${fmt} -> $export_path"
      run_core convert --input "$output_path" -f "$fmt" -o "$export_path"
    done
  fi
fi

if [[ "$dry_run" == true || "$dry_run_only" == true ]]; then
  if [[ -z "$game_dir" || -z "$source_dir" ]]; then
    echo "error: --dry-run and --dry-run-only require --game-dir and --source-dir" >&2
    exit 1
  fi
  # shellcheck source=common.sh
  source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
  ensure_core_resources_symlink "$repo_root"
  if [[ -x "$repo_root/scripts/agents/ensure_linux_holopatcher.sh" ]]; then
    "$repo_root/scripts/agents/ensure_linux_holopatcher.sh" || true
  fi
  validate_args=(
    --input "$output_path"
    --game-dir "$game_dir"
    --source-dir "$source_dir"
  )
  if [[ "$dry_run_only" == true ]]; then
    validate_args+=(--dry-run-only)
  else
    validate_args+=(--dry-run)
  fi
  "$repo_root/scripts/agents/cli_validate.sh" "${validate_args[@]}"
fi

if [[ "$install" == true ]]; then
  if [[ -z "$game_dir" || -z "$source_dir" ]]; then
    echo "error: --install requires --game-dir and --source-dir" >&2
    exit 1
  fi
  # shellcheck source=common.sh
  source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
  ensure_core_resources_symlink "$repo_root"
  if [[ -x "$repo_root/scripts/agents/ensure_linux_holopatcher.sh" ]]; then
    "$repo_root/scripts/agents/ensure_linux_holopatcher.sh" || true
  fi
  echo "Running best-effort install from merged instruction file..."
  exec dotnet run --project "$repo_root/src/KOTORModSync.Core/KOTORModSync.Core.csproj" -f net9.0 -- \
    install -i "$output_path" -g "$game_dir" -s "$source_dir" \
    -d --concurrent --best-effort --skip-validation --download-timeout-hours 72
fi

echo "Merged instruction file: $output_path"
