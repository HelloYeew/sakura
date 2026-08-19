#!/bin/bash
set -eu

# Ensure this perfectly matches a release file at https://ffmpeg.org/releases/
FFMPEG_VERSION="8.1.1"
FFMPEG_FILE="ffmpeg-$FFMPEG_VERSION.tar.gz"

FFMPEG_FLAGS=(
    --disable-static
    --enable-shared
    --disable-debug
    --disable-all
    --disable-autodetect
    --enable-lto

    # Libraries
    --enable-avcodec
    --enable-avformat
    --enable-avutil
    --enable-swscale

    # It is here because FFmpeg's native Opus decoder declares "opus_decoder_deps=swresample", and
    # configure silently drops any decoder whose dependencies are unmet. Without this, the 'opus'
    # entry in the decoder list below is accepted and then discarded.
    --enable-swresample

    # Video formats, parsers & decoders
    --enable-demuxer='avi,flv,asf,mov,matroska'
    --enable-parser='mpeg4video,h264,hevc,vp8,vp9'
    --enable-decoder='flv,msmpeg4v1,msmpeg4v2,msmpeg4v3,mpeg4,vp6,vp6f,wmv2,h264,hevc,vp8,vp9'

    # Audio formats, parsers & decoders for the SDL audio backend
    --enable-demuxer='mp3,ogg,flac,wav,aiff'
    --enable-parser='mpegaudio,flac,vorbis,aac,opus'
    --enable-decoder='mp1,mp1float,mp2,mp2float,mp3,mp3float,vorbis,flac,aac,alac,opus'
    --enable-decoder='pcm_s8,pcm_u8,pcm_s16le,pcm_s16be,pcm_s24le,pcm_s32le,pcm_f32le,pcm_f64le'
    --enable-decoder='pcm_alaw,pcm_mulaw,adpcm_ms,adpcm_ima_wav'

    # Protocols
    --enable-protocol=pipe
    --enable-protocol=file
)

function prep_ffmpeg() {
    FFMPEG_FLAGS+=(
        --prefix="$PWD/$1"
        --shlibdir="$PWD/$1"
    )

    local build_dir="$1-build"
    if [ ! -e "$FFMPEG_FILE" ]; then
        echo "-> Downloading $FFMPEG_FILE..."
        curl -o "$FFMPEG_FILE" "https://ffmpeg.org/releases/$FFMPEG_FILE"
    fi

    if [ ! -d "$build_dir" ]; then
        echo "-> Unpacking source to $build_dir..."
        mkdir "$build_dir"
        tar xzf "$FFMPEG_FILE" --strip 1 -C "$build_dir"
    fi
    cd "$build_dir"
}

function build_ffmpeg() {
    echo "-> Configuring..."
    ./configure "${FFMPEG_FLAGS[@]}"
    echo "-> Building using $CORES threads..."
    make -j$CORES
    make install-libs
}

CORES=0
if [[ "$OSTYPE" == "darwin"* ]]; then
    CORES=$(sysctl -n hw.ncpu)
else
    CORES=$(nproc)
fi
