#!/bin/sh
set -eu

target_app="/Applications/MateView Guardian.app"
executable="$target_app/Contents/MacOS/MateViewGuardian.App"
launch_agent="$HOME/Library/LaunchAgents/com.mateview.guardian.plist"
settings_dir="$HOME/Library/Application Support/MateViewGuardian"

if [ -x "$executable" ]; then
    if ! "$executable" --restore-and-exit; then
        echo "Restore failed. MateView Guardian was left installed so it can be retried." >&2
        exit 1
    fi
fi
/usr/bin/pkill -f "$target_app/Contents/" 2>/dev/null || true
/bin/launchctl unload "$launch_agent" 2>/dev/null || true
rm -f "$launch_agent"
rm -rf "$settings_dir"

if ! rm -rf "$target_app" 2>/dev/null; then
    osascript -e 'do shell script "rm -rf /Applications/MateView\\ Guardian.app" with administrator privileges'
fi

echo "MateView Guardian was removed. The MateView touch strip was restored first."
