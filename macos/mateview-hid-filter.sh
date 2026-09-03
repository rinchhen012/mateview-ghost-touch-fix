#!/bin/sh
set -eu

hidutil_bin=${HIDUTIL_BIN:-/usr/bin/hidutil}
match='{"VendorID":0x12d1,"ProductID":0x10b6}'
mapping='{"UserKeyMapping":[{"HIDKeyboardModifierMappingSrc":0xC000000E9,"HIDKeyboardModifierMappingDst":0x700000000},{"HIDKeyboardModifierMappingSrc":0xC000000EA,"HIDKeyboardModifierMappingDst":0x700000000},{"HIDKeyboardModifierMappingSrc":0xC000000CD,"HIDKeyboardModifierMappingDst":0x700000000},{"HIDKeyboardModifierMappingSrc":0xC000000B1,"HIDKeyboardModifierMappingDst":0x700000000}]}'

mateview_present() {
    "$hidutil_bin" list 2>/dev/null | grep -E '^0x12d1[[:space:]]+0x10b6([[:space:]]|$)' >/dev/null
}

mapping_active() {
    property_output=$("$hidutil_bin" property --matching "$match" --get UserKeyMapping 2>/dev/null) || return 1
    for source in 51539607785 51539607786 51539607757 51539607729; do
        printf '%s\n' "$property_output" | grep -F "HIDKeyboardModifierMappingSrc = $source" >/dev/null || return 1
    done
}

apply_filter() {
    if ! mateview_present; then
        printf '%s\n' 'absent'
        return 2
    fi

    "$hidutil_bin" property --matching "$match" --set "$mapping" >/dev/null
    printf '%s\n' 'active'
}

clear_filter() {
    if mateview_present; then
        "$hidutil_bin" property --matching "$match" --set '{"UserKeyMapping":[]}' >/dev/null
    fi
    printf '%s\n' 'inactive'
}

show_status() {
    if ! mateview_present; then
        printf '%s\n' 'absent'
        return 2
    fi

    if mapping_active; then
        printf '%s\n' 'active'
    else
        printf '%s\n' 'inactive'
    fi
}

watch_filter() {
    check_seconds=${MATEVIEW_CHECK_SECONDS:-5}

    while :; do
        if mateview_present && ! mapping_active; then
            apply_filter >/dev/null || true
        fi
        sleep "$check_seconds"
    done
}

usage() {
    printf 'Usage: %s apply|clear|status|watch\n' "$(basename "$0")" >&2
    exit 64
}

case ${1:-} in
    apply) apply_filter ;;
    clear) clear_filter ;;
    status) show_status ;;
    watch) watch_filter ;;
    *) usage ;;
esac
