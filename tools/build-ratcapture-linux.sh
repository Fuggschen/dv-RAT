#!/usr/bin/env bash
# Builds ratcapture-linux, the PC audio helper, as one binary needing only glibc 2.17 or later.
# Writes RATCapture/linux/out/ratcapture-linux.
#
# Run from WSL:   bash tools/build-ratcapture-linux.sh
# Downloads once: zig into ~/dvrat-zig (about 45 MB).

set -euo pipefail

ZIG_VERSION="${ZIG_VERSION:-0.13.0}"
ZIG_DIR="$HOME/dvrat-zig/zig-linux-x86_64-$ZIG_VERSION"
ZIG_URL="https://ziglang.org/download/$ZIG_VERSION/zig-linux-x86_64-$ZIG_VERSION.tar.xz"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$REPO_DIR/RATCapture/linux"
OUT_DIR="$SRC_DIR/out"
STAGE_DIR="$HOME/dvrat-zig/out"

if [ ! -x "$ZIG_DIR/zig" ]; then
    mkdir -p "$HOME/dvrat-zig"
    echo "Downloading $ZIG_URL"
    curl --fail --location --proto '=https' --tlsv1.2 -o "$HOME/dvrat-zig/zig.tar.xz" "$ZIG_URL"
    tar -xf "$HOME/dvrat-zig/zig.tar.xz" -C "$HOME/dvrat-zig"
    rm "$HOME/dvrat-zig/zig.tar.xz"
fi

mkdir -p "$STAGE_DIR"
# Non-PIE: old Wine releases refused to start PIE executables.
"$ZIG_DIR/zig" cc -target x86_64-linux-gnu.2.17 -O2 -Wall -Wextra -fno-PIE -no-pie \
    -o "$STAGE_DIR/ratcapture-linux" "$SRC_DIR/ratcapture.c" "$SRC_DIR/mpris.c" -ldl -lm -lpthread

newest="$(objdump -T "$STAGE_DIR/ratcapture-linux" | grep -o 'GLIBC_[0-9.]*' | sort -uV | tail -n 1)"
echo "Newest glibc symbol: $newest"
case "$newest" in
    GLIBC_2.1[0-7]|GLIBC_2.[0-9]|GLIBC_2.[0-9].*|GLIBC_2.1[0-7].*) ;;
    *) echo "Needs a glibc newer than 2.17: $newest" >&2; exit 1 ;;
esac
file "$STAGE_DIR/ratcapture-linux" || true

# Some Windows drives (WinBtrfs) refuse WSL writes, so fall back to PowerShell.
if ! { mkdir -p "$OUT_DIR" && cp "$STAGE_DIR/ratcapture-linux" "$OUT_DIR/"; } 2> /dev/null; then
    echo "WSL cannot write to $OUT_DIR; copying with Windows PowerShell instead"
    powershell.exe -NoProfile -NonInteractive -Command \
        "New-Item -ItemType Directory -Force -Path '$(wslpath -w "$OUT_DIR")' | Out-Null; Copy-Item -Force -Path '$(wslpath -w "$STAGE_DIR/ratcapture-linux")' -Destination '$(wslpath -w "$OUT_DIR")'"
fi
echo "Done: $OUT_DIR/ratcapture-linux ($(du -h "$STAGE_DIR/ratcapture-linux" | cut -f1))"
