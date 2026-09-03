#!/bin/sh
set -eu

repo_root=$(CDPATH='' cd -- "$(dirname "$0")/.." && pwd)
output_dir=${1:-"$repo_root/dist"}
mkdir -p "$output_dir"
output_dir=$(CDPATH='' cd -- "$output_dir" && pwd)

staging_root=$(mktemp -d "${TMPDIR:-/tmp}/mateview-package.XXXXXX")
trap 'rm -rf "$staging_root"' EXIT HUP INT TERM

mac_dir="$staging_root/mateview-fix-macos"
windows_dir="$staging_root/mateview-fix-windows"
mkdir -p "$mac_dir" "$windows_dir"

for file in \
    README.txt \
    install.sh \
    uninstall.sh \
    mateview-hid-filter.sh \
    com.mateview-ghost-touch-fix.plist.template
do
    cp "$repo_root/macos/$file" "$mac_dir/$file"
done
chmod 755 "$mac_dir/install.sh" "$mac_dir/uninstall.sh" "$mac_dir/mateview-hid-filter.sh"

for file in \
    README.txt \
    Install.cmd \
    Uninstall.cmd \
    Install-MateViewFix.ps1 \
    MateViewFix.ps1 \
    MateViewFix.psm1
do
    cp "$repo_root/windows/$file" "$windows_dir/$file"
done

(
    cd "$staging_root"
    zip -q -r "$staging_root/mateview-fix-macos.zip" mateview-fix-macos
    zip -q -r "$staging_root/mateview-fix-windows.zip" mateview-fix-windows
)

mv -f "$staging_root/mateview-fix-macos.zip" "$output_dir/mateview-fix-macos.zip"
mv -f "$staging_root/mateview-fix-windows.zip" "$output_dir/mateview-fix-windows.zip"

printf 'Created %s\n' "$output_dir/mateview-fix-macos.zip"
printf 'Created %s\n' "$output_dir/mateview-fix-windows.zip"
