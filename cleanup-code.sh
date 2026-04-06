#!/usr/bin/env bash
# Code style verification/fix for CI and local use. Matches .editorconfig (including end_of_line).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

solution="KOTORModSync.sln"
mode=""

for arg in "$@"; do
  case "$arg" in
    --verify|-Verify) mode="verify" ;;
    --fix|-Fix) mode="fix" ;;
  esac
done

if [[ -z "$mode" ]]; then
  echo "Usage: $0 --verify | --fix" >&2
  exit 2
fi

if [[ "$mode" == "verify" ]]; then
  dotnet format whitespace --verify-no-changes "$solution"
else
  dotnet format whitespace "$solution"
fi
