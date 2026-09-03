#!/bin/sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd)
subject="$repo_root/macos/mateview-hid-filter.sh"
tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/mateview-hid-test.XXXXXX")
trap 'rm -rf "$tmp_dir"' EXIT HUP INT TERM

fake_hidutil="$tmp_dir/hidutil"
calls_file="$tmp_dir/calls"

cat >"$fake_hidutil" <<'FAKE'
#!/bin/sh
printf '%s\n' "$*" >>"$FAKE_HIDUTIL_CALLS"

if [ "${1:-}" = "list" ]; then
    if [ "${FAKE_MATEVIEW_PRESENT:-1}" = "1" ]; then
        printf '%s\n' '0x12d1 0x10b6 0x110000 65368 1 MateView GT'
    fi
    exit 0
fi

case "$*" in
    *"--get UserKeyMapping"*)
        if [ "${FAKE_MAPPING_STATE:-inactive}" = "active" ]; then
            printf '%s\n' 'HIDKeyboardModifierMappingSrc = 51539607785;'
        else
            printf '%s\n' '(null)'
        fi
        ;;
esac
FAKE
chmod +x "$fake_hidutil"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

assert_contains() {
    haystack=$1
    needle=$2
    printf '%s' "$haystack" | grep -F -- "$needle" >/dev/null || fail "missing: $needle"
}

run_subject() {
    env \
        HIDUTIL_BIN="$fake_hidutil" \
        FAKE_HIDUTIL_CALLS="$calls_file" \
        FAKE_MATEVIEW_PRESENT="${FAKE_MATEVIEW_PRESENT:-1}" \
        FAKE_MAPPING_STATE="${FAKE_MAPPING_STATE:-inactive}" \
        "$subject" "$@"
}

: >"$calls_file"
run_subject apply >/dev/null
calls=$(cat "$calls_file")
assert_contains "$calls" 'property --matching {"VendorID":0x12d1,"ProductID":0x10b6}'
assert_contains "$calls" 'HIDKeyboardModifierMappingSrc":0xC000000E9'
assert_contains "$calls" 'HIDKeyboardModifierMappingSrc":0xC000000EA'
assert_contains "$calls" 'HIDKeyboardModifierMappingSrc":0xC000000CD'
assert_contains "$calls" 'HIDKeyboardModifierMappingSrc":0xC000000B1'
assert_contains "$calls" 'HIDKeyboardModifierMappingDst":0x700000000'

: >"$calls_file"
run_subject clear >/dev/null
calls=$(cat "$calls_file")
assert_contains "$calls" 'property --matching {"VendorID":0x12d1,"ProductID":0x10b6}'
assert_contains "$calls" '{"UserKeyMapping":[]}'

status=$(FAKE_MAPPING_STATE=active run_subject status)
[ "$status" = "active" ] || fail "expected active status, got: $status"

status=$(FAKE_MAPPING_STATE=inactive run_subject status)
[ "$status" = "inactive" ] || fail "expected inactive status, got: $status"

set +e
status=$(FAKE_MATEVIEW_PRESENT=0 run_subject status 2>/dev/null)
exit_code=$?
set -e
[ "$exit_code" -eq 2 ] || fail "expected absent exit 2, got: $exit_code"
[ "$status" = "absent" ] || fail "expected absent status, got: $status"

printf '%s\n' 'PASS: macOS MateView HID filter behavior'
