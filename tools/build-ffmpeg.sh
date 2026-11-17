#!/usr/bin/env bash
# Cross-compiles the small audio-only LGPL ffmpeg.exe for 64-bit Windows in WSL (Ubuntu).
# Writes ffmpeg.exe, the LGPL license and a README into DVRAT/ModFiles/ffmpeg/.
#
# Run from WSL:   bash tools/build-ffmpeg.sh
# Needs once:     sudo apt install build-essential mingw-w64 pkg-config curl xz-utils
#
# Builds in ~/dvrat-ffmpeg-build, as compiling under /mnt is several times slower.

set -euo pipefail

FFMPEG_VERSION="${FFMPEG_VERSION:-7.1.1}"
JOBS="${JOBS:-$(nproc)}"
HOST=x86_64-w64-mingw32

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$REPO_DIR/DVRAT/ModFiles/ffmpeg"
WORK_DIR="$HOME/dvrat-ffmpeg-build"
TARBALL="ffmpeg-$FFMPEG_VERSION.tar.xz"
SOURCE_URL="https://ffmpeg.org/releases/$TARBALL"
SRC_DIR="$WORK_DIR/ffmpeg-$FFMPEG_VERSION"

for tool in "$HOST-gcc" "$HOST-strip" make pkg-config curl tar sha256sum; do
    if ! command -v "$tool" > /dev/null; then
        echo "Missing $tool. Install the build tools first:" >&2
        echo "  sudo apt install build-essential mingw-w64 pkg-config curl xz-utils" >&2
        exit 1
    fi
done

mkdir -p "$WORK_DIR"
cd "$WORK_DIR"
if [ ! -f "$TARBALL" ]; then
    echo "Downloading $SOURCE_URL"
    curl --fail --location --proto '=https' --tlsv1.2 -o "$TARBALL.part" "$SOURCE_URL"
    mv "$TARBALL.part" "$TARBALL"
fi
SOURCE_SHA256="$(sha256sum "$TARBALL" | cut -d' ' -f1)"

# Unpack fresh so old objects never mix with a changed configure line.
rm -rf "$SRC_DIR"
tar -xf "$TARBALL"
cd "$SRC_DIR"

# Only what decoding a music file to float PCM needs. No gpl or nonfree, so it stays LGPL.
# --disable-x86asm drops the nasm dependency.
CONFIGURE_ARGS=(
    --target-os=mingw32
    --arch=x86_64
    --cross-prefix="$HOST-"
    --pkg-config=pkg-config
    --enable-static
    --disable-shared
    --disable-everything
    --disable-autodetect
    --disable-network
    --disable-doc
    --disable-debug
    --disable-x86asm
    --disable-ffplay
    --disable-ffprobe
    --disable-avdevice
    --disable-swscale
    --enable-small
    --enable-w32threads
    --enable-ffmpeg
    --enable-avformat
    --enable-avcodec
    --enable-avfilter
    --enable-swresample
    --enable-protocol=file,pipe
    --enable-demuxer=mov,flac,asf,ogg,matroska,aac,mp3,wav,ape
    --enable-parser=aac,aac_latm,flac,opus,vorbis,mpegaudio
    --enable-decoder=aac,aac_latm,alac,flac,wmav1,wmav2,wmapro,wmalossless,opus,vorbis,mp3,mp3float,ape,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le
    --enable-encoder=pcm_f32le
    --enable-muxer=pcm_f32le
    --enable-filter=aresample,aformat,anull
    --extra-ldflags=-static
)

./configure "${CONFIGURE_ARGS[@]}"
make -j"$JOBS" ffmpeg.exe
"$HOST-strip" ffmpeg.exe

STAGE_DIR="$WORK_DIR/out"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR"
cp ffmpeg.exe "$STAGE_DIR/ffmpeg.exe"
cp COPYING.LGPLv2.1 "$STAGE_DIR/COPYING.LGPLv2.1"

cat > "$STAGE_DIR/README.txt" <<EOF
ffmpeg.exe in this folder decodes music files for the R.A.T. tablet's media player.

It is FFmpeg $FFMPEG_VERSION (https://ffmpeg.org), unmodified, licensed under the GNU Lesser General
Public License 2.1 or later; see COPYING.LGPLv2.1. It was cross-compiled for 64-bit Windows with
MinGW-w64 by tools/build-ffmpeg.sh in the R.A.T. source.

Source code: $SOURCE_URL
Source SHA-256: $SOURCE_SHA256

Configure line:
./configure ${CONFIGURE_ARGS[*]}
EOF

# Some Windows drives (WinBtrfs) refuse WSL writes, so fall back to PowerShell.
if ! { mkdir -p "$OUT_DIR" && cp "$STAGE_DIR"/* "$OUT_DIR/"; } 2> /dev/null; then
    echo "WSL cannot write to $OUT_DIR; copying with Windows PowerShell instead"
    powershell.exe -NoProfile -NonInteractive -Command \
        "New-Item -ItemType Directory -Force -Path '$(wslpath -w "$OUT_DIR")' | Out-Null; Copy-Item -Force -Path '$(wslpath -w "$STAGE_DIR")\\*' -Destination '$(wslpath -w "$OUT_DIR")'"
fi

echo
echo "Done: $OUT_DIR/ffmpeg.exe ($(du -h "$STAGE_DIR/ffmpeg.exe" | cut -f1))"
