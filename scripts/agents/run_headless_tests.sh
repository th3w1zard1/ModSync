#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
configuration="${CONFIGURATION:-Debug}"
filter="FullyQualifiedName!~LongRunning"

usage() {
  cat <<'EOF'
Usage: run_headless_tests.sh [options]

Options:
  --filter EXPR          dotnet test filter (default: exclude LongRunning)
  --configuration NAME   Debug or Release (default: Debug)
  -h, --help             Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --filter)
      filter="$2"
      shift 2
      ;;
    --configuration)
      configuration="$2"
      shift 2
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

dotnet test "$repo_root/src/KOTORModSync.Tests/KOTORModSync.Tests.csproj" \
  --configuration "$configuration" \
  --filter "$filter"
