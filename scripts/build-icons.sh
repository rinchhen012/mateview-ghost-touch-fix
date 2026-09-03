#!/bin/sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
asset_dir="$repo_root/src/MateViewGuardian.App/Assets"

if ! command -v sips >/dev/null 2>&1; then
    echo "sips is required to build Guardian icons on macOS." >&2
    exit 1
fi

temporary_dir=$(mktemp -d "${TMPDIR:-/tmp}/mateview-icons.XXXXXX")
trap 'rm -rf "$temporary_dir"' EXIT HUP INT TERM

for variant in protected:1c8f68 partial:e5a000 error:d13438 disabled:777777; do
    name=${variant%%:*}
    color=${variant#*:}
    sed "s/#1c8f68/#$color/g" "$asset_dir/guardian.svg" > "$temporary_dir/$name.svg"
    sips -s format png -z 256 256 "$temporary_dir/$name.svg" --out "$asset_dir/guardian-$name.png" >/dev/null
    sips -s format ico -z 64 64 "$temporary_dir/$name.svg" --out "$asset_dir/guardian-$name.ico" >/dev/null
done
