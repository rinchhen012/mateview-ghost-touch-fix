#!/bin/sh
set -eu

output_dir=${1:?Usage: fetch-apple-silicon-ddc.sh OUTPUT_DIRECTORY}
git_bin=${GIT_BIN:-git}
swift_bin=${SWIFT_BIN:-swift}
source_url=${APPLE_SILICON_DDC_REPOSITORY:-https://github.com/waydabber/AppleSiliconDDC.git}
commit='67ff964ab8123d9d35fadf7d8e1a7c677d31da14'
temporary_root=$(mktemp -d "${TMPDIR:-/tmp}/guardian-asddc.XXXXXX")
trap 'rm -rf "$temporary_root"' EXIT HUP INT TERM
source_dir="$temporary_root/AppleSiliconDDC"

"$git_bin" clone --filter=blob:none --no-checkout "$source_url" "$source_dir"
"$git_bin" -C "$source_dir" checkout --detach "$commit"
(
    cd "$source_dir"
    "$swift_bin" build -c release --product ASDDC
)

mkdir -p "$output_dir"
cp "$source_dir/.build/release/ASDDC" "$output_dir/ASDDC"
chmod 755 "$output_dir/ASDDC"
cp "$source_dir/LICENSE" "$output_dir/AppleSiliconDDC-LICENSE"

printf 'Prepared pinned ASDDC helper at %s\n' "$output_dir/ASDDC"
