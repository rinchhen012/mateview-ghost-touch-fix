#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
action=${1:-install}
user_home=${MATEVIEW_USER_HOME:-$HOME}
gui_uid=${MATEVIEW_GUI_UID:-$(id -u)}
launchctl_bin=${LAUNCHCTL_BIN:-/bin/launchctl}
label='com.mateview-ghost-touch-fix'
install_dir="$user_home/Library/Application Support/MateViewGhostTouchFix"
launch_agents_dir="$user_home/Library/LaunchAgents"
installed_filter="$install_dir/mateview-hid-filter.sh"
plist="$launch_agents_dir/$label.plist"

xml_escape() {
    printf '%s' "$1" | sed \
        -e 's/&/\&amp;/g' \
        -e 's/</\&lt;/g' \
        -e 's/>/\&gt;/g'
}

render_plist() {
    escaped_path=$(xml_escape "$installed_filter")
    while IFS= read -r line || [ -n "$line" ]; do
        if [ "$line" = '        <string>__PROGRAM_PATH__</string>' ]; then
            printf '        <string>%s</string>\n' "$escaped_path"
        else
            printf '%s\n' "$line"
        fi
    done <"$script_dir/com.mateview-ghost-touch-fix.plist.template"
}

apply_installed_filter() {
    set +e
    "$installed_filter" apply
    apply_status=$?
    set -e
    if [ "$apply_status" -ne 0 ] && [ "$apply_status" -ne 2 ]; then
        exit "$apply_status"
    fi
}

enable_filter() {
    if [ ! -x "$installed_filter" ] || [ ! -f "$plist" ]; then
        printf '%s\n' 'MateView filter is not installed. Run install.sh first.' >&2
        exit 1
    fi
    # Replace an already-loaded copy without treating its absence as an error.
    "$launchctl_bin" bootout "gui/$gui_uid/$label" >/dev/null 2>&1 || true
    "$launchctl_bin" bootstrap "gui/$gui_uid" "$plist"
    apply_installed_filter
    printf '%s\n' 'Enabled MateView ghost-touch filter.'
}

disable_filter() {
    "$launchctl_bin" bootout "gui/$gui_uid/$label" >/dev/null 2>&1 || true
    if [ -x "$installed_filter" ]; then
        "$installed_filter" clear >/dev/null || true
    fi
    printf '%s\n' 'Disabled MateView ghost-touch filter.'
}

case "$action" in
    install)
        mkdir -p "$install_dir" "$launch_agents_dir"
        cp "$script_dir/mateview-hid-filter.sh" "$installed_filter"
        chmod 755 "$installed_filter"
        render_plist >"$plist"
        enable_filter >/dev/null
        printf '%s\n' "Installed MateView ghost-touch filter for user $gui_uid."
        ;;
    enable)
        enable_filter
        ;;
    disable)
        disable_filter
        ;;
    status)
        if [ ! -x "$installed_filter" ]; then
            printf '%s\n' 'not-installed'
            exit 2
        fi
        "$installed_filter" status
        ;;
    *)
        printf 'Usage: %s [install|enable|disable|status]\n' "$(basename "$0")" >&2
        exit 64
        ;;
esac
