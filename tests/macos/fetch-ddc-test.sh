#!/bin/sh
set -eu

repo_root=$(CDPATH='' cd -- "$(dirname "$0")/../.." && pwd)
subject="$repo_root/scripts/fetch-apple-silicon-ddc.sh"
tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/guardian-ddc-fetch.XXXXXX")
trap 'rm -rf "$tmp_dir"' EXIT HUP INT TERM
fake_bin="$tmp_dir/bin"
calls="$tmp_dir/calls"
output="$tmp_dir/output"
mkdir -p "$fake_bin"
: >"$calls"

cat >"$fake_bin/git" <<'FAKE'
#!/bin/sh
printf 'git %s\n' "$*" >>"$FETCH_TEST_CALLS"
if [ "$1" = "clone" ]; then
    destination=''
    for argument in "$@"; do destination=$argument; done
    mkdir -p "$destination"
    printf '%s\n' 'MIT test license' >"$destination/LICENSE"
fi
FAKE

cat >"$fake_bin/swift" <<'FAKE'
#!/bin/sh
printf 'swift %s\n' "$*" >>"$FETCH_TEST_CALLS"
mkdir -p .build/release
printf '%s\n' '#!/bin/sh' 'echo ASDDC' >.build/release/ASDDC
chmod +x .build/release/ASDDC
FAKE
chmod +x "$fake_bin/git" "$fake_bin/swift"

env \
    GIT_BIN="$fake_bin/git" \
    SWIFT_BIN="$fake_bin/swift" \
    FETCH_TEST_CALLS="$calls" \
    "$subject" "$output" >/dev/null

[ -x "$output/ASDDC" ] || { printf '%s\n' 'FAIL: ASDDC missing' >&2; exit 1; }
[ -f "$output/AppleSiliconDDC-LICENSE" ] || { printf '%s\n' 'FAIL: license missing' >&2; exit 1; }
grep -F 'checkout --detach 67ff964ab8123d9d35fadf7d8e1a7c677d31da14' "$calls" >/dev/null || {
    printf '%s\n' 'FAIL: pinned commit not checked out' >&2
    exit 1
}
grep -F 'swift build -c release --product ASDDC' "$calls" >/dev/null || {
    printf '%s\n' 'FAIL: release helper not built' >&2
    exit 1
}

printf '%s\n' 'PASS: pinned AppleSiliconDDC fetch behavior'
