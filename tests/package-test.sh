#!/bin/sh
set -eu

repo_root=$(CDPATH='' cd -- "$(dirname "$0")/.." && pwd)
package_script="$repo_root/scripts/package.sh"
tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/mateview-package-test.XXXXXX")
trap 'rm -rf "$tmp_dir"' EXIT HUP INT TERM

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

assert_entry() {
    archive=$1
    entry=$2
    unzip -Z1 "$archive" | grep -Fx -- "$entry" >/dev/null || fail "missing $entry in $archive"
}

assert_no_entry() {
    archive=$1
    pattern=$2
    if unzip -Z1 "$archive" | grep -E -- "$pattern" >/dev/null; then
        fail "unexpected entry matching $pattern in $archive"
    fi
}

"$package_script" "$tmp_dir"

mac_archive="$tmp_dir/mateview-fix-macos.zip"
windows_archive="$tmp_dir/mateview-fix-windows.zip"
[ -f "$mac_archive" ] || fail 'macOS archive was not created'
[ -f "$windows_archive" ] || fail 'Windows archive was not created'

for entry in \
    'mateview-fix-macos/README.txt' \
    'mateview-fix-macos/install.sh' \
    'mateview-fix-macos/uninstall.sh' \
    'mateview-fix-macos/mateview-hid-filter.sh' \
    'mateview-fix-macos/com.mateview-ghost-touch-fix.plist.template'
do
    assert_entry "$mac_archive" "$entry"
done

for entry in \
    'mateview-fix-windows/README.txt' \
    'mateview-fix-windows/Install.cmd' \
    'mateview-fix-windows/Uninstall.cmd' \
    'mateview-fix-windows/Install-MateViewFix.ps1' \
    'mateview-fix-windows/MateViewFix.ps1' \
    'mateview-fix-windows/MateViewFix.psm1'
do
    assert_entry "$windows_archive" "$entry"
done

assert_no_entry "$mac_archive" '(^|/)\.git|(^|/)tests/|(^|/)docs/'
assert_no_entry "$windows_archive" '(^|/)\.git|(^|/)tests/|(^|/)docs/'

printf '%s\n' 'PASS: release packages contain only platform runtime files'
