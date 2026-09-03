#!/bin/sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd)
install_script="$repo_root/macos/install.sh"
uninstall_script="$repo_root/macos/uninstall.sh"
tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/mateview-install-test.XXXXXX")
trap 'rm -rf "$tmp_dir"' EXIT HUP INT TERM

test_user_home="$tmp_dir/user"
fake_bin="$tmp_dir/bin"
launchctl_calls="$tmp_dir/launchctl-calls"
hidutil_calls="$tmp_dir/hidutil-calls"
mkdir -p "$test_user_home" "$fake_bin"
: >"$launchctl_calls"
: >"$hidutil_calls"

cat >"$fake_bin/launchctl" <<'FAKE'
#!/bin/sh
printf '%s\n' "$*" >>"$FAKE_LAUNCHCTL_CALLS"
exit 0
FAKE

cat >"$fake_bin/hidutil" <<'FAKE'
#!/bin/sh
printf '%s\n' "$*" >>"$FAKE_HIDUTIL_CALLS"
if [ "${1:-}" = "list" ]; then
    printf '%s\n' '0x12d1 0x10b6 0x110000 65368 1 MateView GT'
fi
if [ "${1:-}" = "property" ] && [ "${FAKE_HIDUTIL_FAIL:-0}" = "1" ]; then
    exit 1
fi
exit 0
FAKE
chmod +x "$fake_bin/launchctl" "$fake_bin/hidutil"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

assert_file() {
    [ -f "$1" ] || fail "expected file: $1"
}

assert_not_exists() {
    [ ! -e "$1" ] || fail "expected path to be absent: $1"
}

assert_contains() {
    haystack=$1
    needle=$2
    printf '%s' "$haystack" | grep -F -- "$needle" >/dev/null || fail "missing: $needle"
}

assert_not_contains() {
    haystack=$1
    needle=$2
    if printf '%s' "$haystack" | grep -F -- "$needle" >/dev/null; then
        fail "unexpected: $needle"
    fi
}

run_lifecycle() {
    env \
        MATEVIEW_USER_HOME="$test_user_home" \
        MATEVIEW_GUI_UID=501 \
        LAUNCHCTL_BIN="$fake_bin/launchctl" \
        HIDUTIL_BIN="$fake_bin/hidutil" \
        FAKE_LAUNCHCTL_CALLS="$launchctl_calls" \
        FAKE_HIDUTIL_CALLS="$hidutil_calls" \
        FAKE_HIDUTIL_FAIL="${FAKE_HIDUTIL_FAIL:-0}" \
        "$@"
}

run_lifecycle "$install_script" >/dev/null

install_dir="$test_user_home/Library/Application Support/MateViewGhostTouchFix"
installed_filter="$install_dir/mateview-hid-filter.sh"
plist="$test_user_home/Library/LaunchAgents/com.mateview-ghost-touch-fix.plist"
assert_file "$installed_filter"
assert_file "$plist"
plist_contents=$(cat "$plist")
assert_contains "$plist_contents" "<string>$installed_filter</string>"
assert_contains "$plist_contents" '<string>watch</string>'
assert_contains "$plist_contents" '<key>RunAtLoad</key>'
assert_contains "$plist_contents" '<key>KeepAlive</key>'
assert_contains "$(cat "$launchctl_calls")" "bootstrap gui/501 $plist"
assert_contains "$(cat "$hidutil_calls")" 'HIDKeyboardModifierMappingSrc":0xC000000E9'

: >"$launchctl_calls"
: >"$hidutil_calls"
run_lifecycle "$install_script" disable >/dev/null
assert_contains "$(cat "$launchctl_calls")" 'bootout gui/501/com.mateview-ghost-touch-fix'
assert_not_contains "$(cat "$launchctl_calls")" 'bootstrap gui/501'
assert_contains "$(cat "$hidutil_calls")" '{"UserKeyMapping":[]}'

: >"$launchctl_calls"
run_lifecycle "$install_script" enable >/dev/null
assert_contains "$(cat "$launchctl_calls")" "bootstrap gui/501 $plist"

run_lifecycle "$uninstall_script" >/dev/null
assert_not_exists "$plist"
assert_not_exists "$install_dir"
assert_contains "$(cat "$launchctl_calls")" 'bootout gui/501/com.mateview-ghost-touch-fix'
assert_contains "$(cat "$hidutil_calls")" '{"UserKeyMapping":[]}'

# A second uninstall must be safe and successful.
run_lifecycle "$uninstall_script" >/dev/null

# A real hidutil failure must not be reported as a successful install.
set +e
FAKE_HIDUTIL_FAIL=1 run_lifecycle "$install_script" >/dev/null 2>&1
failure_status=$?
set -e
[ "$failure_status" -ne 0 ] || fail 'expected install to propagate hidutil failure'
FAKE_HIDUTIL_FAIL=0 run_lifecycle "$uninstall_script" >/dev/null

printf '%s\n' 'PASS: macOS install and uninstall behavior'
