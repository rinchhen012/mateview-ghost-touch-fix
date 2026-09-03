#!/bin/sh
set -eu

repo_root=$(CDPATH='' cd -- "$(dirname "$0")/.." && pwd)
temporary_dir=$(mktemp -d "${TMPDIR:-/tmp}/guardian-package-test.XXXXXX")
trap 'rm -rf "$temporary_dir"' EXIT HUP INT TERM

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

assert_entry() {
    archive=$1
    entry=$2
    unzip -Z1 "$archive" | grep -Fx -- "$entry" >/dev/null || fail "missing $entry"
}

fake_ddc="$temporary_dir/ASDDC"
fake_license="$temporary_dir/AppleSiliconDDC-LICENSE"
printf '#!/bin/sh\nexit 0\n' > "$fake_ddc"
printf '%s\n' 'MIT test fixture' > "$fake_license"
chmod 755 "$fake_ddc"

MATEVIEW_DDC_BINARY="$fake_ddc" \
MATEVIEW_DDC_LICENSE="$fake_license" \
    "$repo_root/scripts/package-gui.sh" "$temporary_dir/dist"

mac_archive="$temporary_dir/dist/MateViewGuardian-macOS-arm64.zip"
windows_archive="$temporary_dir/dist/MateViewGuardian-Windows-x64.zip"
[ -f "$mac_archive" ] || fail 'macOS GUI archive was not created'
[ -f "$windows_archive" ] || fail 'Windows GUI archive was not created'

for entry in \
    'MateViewGuardian-macOS-arm64/MateView Guardian.app/Contents/Info.plist' \
    'MateViewGuardian-macOS-arm64/MateView Guardian.app/Contents/MacOS/MateViewGuardian.App' \
    'MateViewGuardian-macOS-arm64/MateView Guardian.app/Contents/Resources/ASDDC' \
    'MateViewGuardian-macOS-arm64/Install.command' \
    'MateViewGuardian-macOS-arm64/Uninstall.command' \
    'MateViewGuardian-macOS-arm64/THIRD_PARTY_NOTICES.md'
do
    assert_entry "$mac_archive" "$entry"
done

for entry in \
    'MateViewGuardian-Windows-x64/MateViewGuardian.App.exe' \
    'MateViewGuardian-Windows-x64/platform-tools/windows/MateViewHid.ps1' \
    'MateViewGuardian-Windows-x64/Install.cmd' \
    'MateViewGuardian-Windows-x64/Uninstall.cmd' \
    'MateViewGuardian-Windows-x64/THIRD_PARTY_NOTICES.md'
do
    assert_entry "$windows_archive" "$entry"
done

if { unzip -Z1 "$mac_archive"; unzip -Z1 "$windows_archive"; } |
    grep -E '(^|/)(\.git|tests|docs|obj|bin)(/|$)' >/dev/null; then
    fail 'archive contains repository or build metadata'
fi

extract_dir="$temporary_dir/extracted"
mkdir -p "$extract_dir"
unzip -q "$mac_archive" -d "$extract_dir"
[ -x "$extract_dir/MateViewGuardian-macOS-arm64/MateView Guardian.app/Contents/MacOS/MateViewGuardian.App" ] ||
    fail 'macOS app host is not executable'
[ -x "$extract_dir/MateViewGuardian-macOS-arm64/MateView Guardian.app/Contents/Resources/ASDDC" ] ||
    fail 'macOS DDC helper is not executable'

(
    cd "$temporary_dir/dist"
    shasum -a 256 -c SHA256SUMS.txt
)

printf '%s\n' 'PASS: GUI packages contain self-contained runtimes and recovery tools'
