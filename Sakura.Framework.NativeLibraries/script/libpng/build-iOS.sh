#!/bin/bash
set -eu

# Builds libpng as a static xcframework for iOS (device arm64; simulator arm64 and x86_64).
#
# FreeTypeSharp's iOS FreeType is a static archive built with PNG support, for colour emoji, but without libpng
# itself, so an app linking it fails on the undefined png_* symbols. This supplies them. The version is the one
# that FreeType was built against (its archive carries the string "1.6.50"), and the archive is linked into the
# app next to FreeType's.
#
# Usage: build-iOS.sh [output directory]. Writes <output directory>/libpng16.xcframework, by default in
# ./iOS-libpng.

LIBPNG_VERSION="1.6.50"
LIBPNG_SHA256="71158e53cfdf2877bc99bcab33641d78df3f48e6e0daad030afe9cb8c031aa46"

# Minimum iOS version. This should be the same as in Sakura.Framework.iOS.csproj
DEPLOYMENT_TARGET="15.0"

output_dir="$(cd "$(dirname "${1:-iOS-libpng}")" && pwd)/$(basename "${1:-iOS-libpng}")"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

archive="$work_dir/libpng-$LIBPNG_VERSION.tar.gz"
curl -fsSL "https://github.com/pnggroup/libpng/archive/refs/tags/v$LIBPNG_VERSION.tar.gz" -o "$archive"

if [ "$(shasum -a 256 "$archive" | cut -d ' ' -f 1)" != "$LIBPNG_SHA256" ]; then
    echo "libpng $LIBPNG_VERSION doesn't match its checksum"
    exit 1
fi

tar -xzf "$archive" -C "$work_dir"
source_dir="$work_dir/libpng-$LIBPNG_VERSION"

# build <name> <sdk> <arch>
build() {
    local name=$1 sdk=$2 arch=$3
    local sysroot
    sysroot=$(xcrun --sdk "$sdk" --show-sdk-path)

    cmake -S "$source_dir" -B "$work_dir/build-$name" -G "Unix Makefiles" \
        -DCMAKE_SYSTEM_NAME=iOS \
        -DCMAKE_OSX_SYSROOT="$sysroot" \
        -DCMAKE_OSX_ARCHITECTURES="$arch" \
        -DCMAKE_OSX_DEPLOYMENT_TARGET="$DEPLOYMENT_TARGET" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_C_FLAGS="-fvisibility=hidden" \
        -DZLIB_INCLUDE_DIR="$sysroot/usr/include" \
        -DZLIB_LIBRARY="$sysroot/usr/lib/libz.tbd" \
        -DPNG_SHARED=OFF \
        -DPNG_STATIC=ON \
        -DPNG_FRAMEWORK=OFF \
        -DPNG_TESTS=OFF \
        -DPNG_TOOLS=OFF

    cmake --build "$work_dir/build-$name" --target png_static -j "$(sysctl -n hw.ncpu)"
}

build device iphoneos arm64
build simulator-arm64 iphonesimulator arm64
build simulator-x86_64 iphonesimulator x86_64

mkdir -p "$work_dir/device" "$work_dir/simulator" "$work_dir/headers"
cp "$work_dir/build-device/libpng16.a" "$work_dir/device/libpng16.a"
lipo -create "$work_dir/build-simulator-arm64/libpng16.a" "$work_dir/build-simulator-x86_64/libpng16.a" \
    -output "$work_dir/simulator/libpng16.a"
cp "$source_dir/png.h" "$source_dir/pngconf.h" "$work_dir/build-device/pnglibconf.h" "$work_dir/headers/"

rm -rf "$output_dir/libpng16.xcframework"
mkdir -p "$output_dir"
xcodebuild -create-xcframework \
    -library "$work_dir/device/libpng16.a" -headers "$work_dir/headers" \
    -library "$work_dir/simulator/libpng16.a" -headers "$work_dir/headers" \
    -output "$output_dir/libpng16.xcframework"
