#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <kotor_dir> <mod_dir>" >&2
  exit 1
fi

kotor_dir="$1"
mod_dir="$2"

mkdir -p \
  "$kotor_dir/data" \
  "$kotor_dir/lips" \
  "$kotor_dir/modules/extras" \
  "$kotor_dir/movies" \
  "$kotor_dir/Override" \
  "$kotor_dir/rims" \
  "$kotor_dir/streammusic" \
  "$kotor_dir/streamsounds" \
  "$kotor_dir/streamwaves/globe" \
  "$kotor_dir/TexturePacks" \
  "$kotor_dir/utils/swupdateskins" \
  "$mod_dir"

printf 'fake exe\n' > "$kotor_dir/swkotor.exe"
printf 'fake dialog\n' > "$kotor_dir/dialog.tlk"

cat <<EOF
Created template KOTOR install:
  kotor_dir=$kotor_dir
  mod_dir=$mod_dir
EOF
