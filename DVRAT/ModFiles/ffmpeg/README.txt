ffmpeg.exe in this folder decodes music files for the R.A.T. tablet's media player.

It is FFmpeg 7.1.1 (https://ffmpeg.org), unmodified, licensed under the GNU Lesser General
Public License 2.1 or later; see COPYING.LGPLv2.1. It was cross-compiled for 64-bit Windows with
MinGW-w64 by tools/build-ffmpeg.sh in the R.A.T. source.

Source code: https://ffmpeg.org/releases/ffmpeg-7.1.1.tar.xz
Source SHA-256: 733984395e0dbbe5c046abda2dc49a5544e7e0e1e2366bba849222ae9e3a03b1

Configure line:
./configure --target-os=mingw32 --arch=x86_64 --cross-prefix=x86_64-w64-mingw32- --pkg-config=pkg-config --enable-static --disable-shared --disable-everything --disable-autodetect --disable-network --disable-doc --disable-debug --disable-x86asm --disable-ffplay --disable-ffprobe --disable-avdevice --disable-swscale --enable-small --enable-w32threads --enable-ffmpeg --enable-avformat --enable-avcodec --enable-avfilter --enable-swresample --enable-protocol=file,pipe --enable-demuxer=mov,flac,asf,ogg,matroska,aac,mp3,wav,ape --enable-parser=aac,aac_latm,flac,opus,vorbis,mpegaudio --enable-decoder=aac,aac_latm,alac,flac,wmav1,wmav2,wmapro,wmalossless,opus,vorbis,mp3,mp3float,ape,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le --enable-encoder=pcm_f32le --enable-muxer=pcm_f32le --enable-filter=aresample,aformat,anull --extra-ldflags=-static
