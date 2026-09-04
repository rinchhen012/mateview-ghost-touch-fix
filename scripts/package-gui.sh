#!/bin/sh
set -eu

repo_root=$(CDPATH='' cd -- "$(dirname "$0")/.." && pwd)
output_dir=${1:-"$repo_root/dist"}
mkdir -p "$output_dir"
output_dir=$(CDPATH='' cd -- "$output_dir" && pwd)

staging_root=$(mktemp -d "${TMPDIR:-/tmp}/guardian-package.XXXXXX")
trap 'rm -rf "$staging_root"' EXIT HUP INT TERM
publish_mac="$staging_root/publish-mac"
publish_windows="$staging_root/publish-windows"
ddc_dir="$staging_root/ddc"
mac_root="$staging_root/MateViewGuardian-macOS-arm64"
windows_root="$staging_root/MateViewGuardian-Windows-x64"
app_root="$mac_root/MateView Guardian.app"

"$repo_root/scripts/build-icons.sh"
iconset_dir="$staging_root/Guardian.iconset"
mkdir -p "$iconset_dir"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$repo_root/src/MateViewGuardian.App/Assets/guardian-protected.png" \
        --out "$iconset_dir/icon_${size}x${size}.png" >/dev/null
    doubled_size=$((size * 2))
    sips -z "$doubled_size" "$doubled_size" "$repo_root/src/MateViewGuardian.App/Assets/guardian-protected.png" \
        --out "$iconset_dir/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset_dir" -o "$staging_root/Guardian.icns"
swiftc -framework AppKit "$repo_root/platform-tools/macos/MateViewGuardianMenuBar.swift" \
    -o "$staging_root/MateViewGuardianMenuBar"

if [ -n "${MATEVIEW_DDC_BINARY:-}" ]; then
    [ -x "$MATEVIEW_DDC_BINARY" ] || {
        echo 'MATEVIEW_DDC_BINARY must point to an executable.' >&2
        exit 1
    }
    [ -f "${MATEVIEW_DDC_LICENSE:-}" ] || {
        echo 'MATEVIEW_DDC_LICENSE must point to the matching license.' >&2
        exit 1
    }
    mkdir -p "$ddc_dir"
    cp "$MATEVIEW_DDC_BINARY" "$ddc_dir/ASDDC"
    cp "$MATEVIEW_DDC_LICENSE" "$ddc_dir/AppleSiliconDDC-LICENSE"
else
    "$repo_root/scripts/fetch-apple-silicon-ddc.sh" "$ddc_dir"
fi

dotnet publish "$repo_root/src/MateViewGuardian.App/MateViewGuardian.App.csproj" \
    -c Release -r osx-arm64 --self-contained true --nologo \
    -p:Version=0.2.11 -p:DebugType=None -p:DebugSymbols=false \
    -o "$publish_mac"
dotnet publish "$repo_root/src/MateViewGuardian.App/MateViewGuardian.App.csproj" \
    -c Release -r win-x64 --self-contained true --nologo \
    -p:Version=0.2.11 -p:DebugType=None -p:DebugSymbols=false \
    -o "$publish_windows"

mkdir -p "$app_root/Contents/MacOS" "$app_root/Contents/Resources/app"
cp -R "$publish_mac/." "$app_root/Contents/Resources/app/"
cp "$repo_root/packaging/macos/MateViewGuardian.App" "$app_root/Contents/MacOS/MateViewGuardian.App"
cp "$repo_root/packaging/macos/Info.plist" "$app_root/Contents/Info.plist"
cp "$staging_root/Guardian.icns" "$app_root/Contents/Resources/Guardian.icns"
cp "$staging_root/MateViewGuardianMenuBar" "$app_root/Contents/Resources/MateViewGuardianMenuBar"
cp "$ddc_dir/ASDDC" "$app_root/Contents/Resources/ASDDC"
cp "$ddc_dir/AppleSiliconDDC-LICENSE" "$app_root/Contents/Resources/AppleSiliconDDC-LICENSE"
chmod 755 \
    "$app_root/Contents/MacOS/MateViewGuardian.App" \
    "$app_root/Contents/Resources/app/MateViewGuardian.App" \
    "$app_root/Contents/Resources/MateViewGuardianMenuBar" \
    "$app_root/Contents/Resources/ASDDC"
cp "$repo_root/packaging/macos/Install.command" "$mac_root/Install.command"
cp "$repo_root/packaging/macos/Uninstall.command" "$mac_root/Uninstall.command"
cp "$repo_root/packaging/macos/README.txt" "$mac_root/README.txt"
cp "$repo_root/THIRD_PARTY_NOTICES.md" "$mac_root/THIRD_PARTY_NOTICES.md"
chmod 755 "$mac_root/Install.command" "$mac_root/Uninstall.command"

if command -v codesign >/dev/null 2>&1; then
    find "$app_root/Contents/Resources" -type f -print | while IFS= read -r candidate; do
        if file "$candidate" | grep -q 'Mach-O'; then
            codesign --force --sign - "$candidate"
        fi
    done
    codesign --force --sign - "$app_root"
fi

mkdir -p "$windows_root/platform-tools/windows"
cp -R "$publish_windows/." "$windows_root/"
cp "$repo_root/platform-tools/windows/MateViewHid.ps1" "$windows_root/platform-tools/windows/MateViewHid.ps1"
cp "$repo_root/packaging/windows/Install.cmd" "$windows_root/Install.cmd"
cp "$repo_root/packaging/windows/Uninstall.cmd" "$windows_root/Uninstall.cmd"
cp "$repo_root/packaging/windows/README.txt" "$windows_root/README.txt"
cp "$repo_root/THIRD_PARTY_NOTICES.md" "$windows_root/THIRD_PARTY_NOTICES.md"

(
    cd "$staging_root"
    zip -q -r "$staging_root/MateViewGuardian-macOS-arm64.zip" "MateViewGuardian-macOS-arm64"
    zip -q -r "$staging_root/MateViewGuardian-Windows-x64.zip" "MateViewGuardian-Windows-x64"
)

mv -f "$staging_root/MateViewGuardian-macOS-arm64.zip" "$output_dir/MateViewGuardian-macOS-arm64.zip"
mv -f "$staging_root/MateViewGuardian-Windows-x64.zip" "$output_dir/MateViewGuardian-Windows-x64.zip"
(
    cd "$output_dir"
    shasum -a 256 MateViewGuardian-macOS-arm64.zip MateViewGuardian-Windows-x64.zip > SHA256SUMS.txt
)

printf 'Created Guardian GUI packages in %s\n' "$output_dir"
