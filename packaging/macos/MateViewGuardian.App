#!/bin/sh
set -eu

macos_dir=$(cd -- "$(dirname -- "$0")" && pwd)
exec "$macos_dir/../Resources/app/MateViewGuardian.App" "$@"
