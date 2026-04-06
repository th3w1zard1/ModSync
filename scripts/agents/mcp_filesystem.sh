#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

exec npx -y @modelcontextprotocol/server-filesystem@2026.1.14 "$repo_root"
