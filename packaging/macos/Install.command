#!/bin/sh
set -eu

package_dir=$(cd -- "$(dirname -- "$0")" && pwd)
source_app="$package_dir/MateView Guardian.app"
target_app="/Applications/MateView Guardian.app"

if [ ! -d "$source_app" ]; then
    echo "MateView Guardian.app is missing from this package." >&2
    exit 1
fi

if ! ditto "$source_app" "$target_app" 2>/dev/null; then
    quoted_source=$(printf %s "$source_app" | sed "s/'/'\\''/g")
    quoted_target=$(printf %s "$target_app" | sed "s/'/'\\''/g")
    osascript -e "do shell script \"ditto '$quoted_source' '$quoted_target'\" with administrator privileges"
fi

open "$target_app"
echo "MateView Guardian was installed in /Applications and opened."
