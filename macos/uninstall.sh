#!/bin/sh
set -eu

user_home=${MATEVIEW_USER_HOME:-$HOME}
gui_uid=${MATEVIEW_GUI_UID:-$(id -u)}
launchctl_bin=${LAUNCHCTL_BIN:-/bin/launchctl}
label='com.mateview-ghost-touch-fix'
install_dir="$user_home/Library/Application Support/MateViewGhostTouchFix"
installed_filter="$install_dir/mateview-hid-filter.sh"
plist="$user_home/Library/LaunchAgents/$label.plist"

"$launchctl_bin" bootout "gui/$gui_uid/$label" >/dev/null 2>&1 || true

if [ -x "$installed_filter" ]; then
    "$installed_filter" clear >/dev/null || true
fi

rm -f "$plist"
rm -rf "$install_dir"

printf '%s\n' 'Removed MateView ghost-touch filter.'
