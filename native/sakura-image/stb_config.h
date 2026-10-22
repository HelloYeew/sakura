// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

// The stb configuration, in one place because it has to be identical in every translation unit that
// includes the vendored headers -- stb_impl.c, which instantiates them, and sakura_image.c, which only
// takes the declarations. A define that appears in one and not the other is an ODR violation that
// links cleanly and misbehaves at run time.

#ifndef SAKURA_IMAGE_STB_CONFIG_H
#define SAKURA_IMAGE_STB_CONFIG_H

// stb keeps its last error message in one global unless told otherwise, and textures are decoded on
// background threads, so two concurrent failures would race on it. Set per compiler rather than left
// to stb's own detection, so the choice is the same on every leg of CI instead of depending on what
// the compiler happens to advertise.
#if defined(_MSC_VER)
#define STBI_THREAD_LOCAL __declspec(thread)
#else
#define STBI_THREAD_LOCAL _Thread_local
#endif

// No file I/O: every entry point takes a buffer the managed side already holds, and linking the stdio
// paths would only add surface.
#define STBI_NO_STDIO

// Beatmap archives are user-supplied, so the decoders compiled in are the ones actually exercised.
// ImageSharp remains the fallback and covers everything left out (WebP, TIFF, TGA, QOI, PBM), so
// restricting stb costs no format support -- a rejection here routes to ImageSharp rather than failing
// the load.
#define STBI_ONLY_JPEG
#define STBI_ONLY_PNG
#define STBI_ONLY_BMP
#define STBI_ONLY_GIF

// Well past any GPU's maximum texture size, so a header claiming an absurd size is refused before the
// allocation is attempted rather than after.
#define STBI_MAX_DIMENSIONS 32768

// stb_image leaves its NEON loops (JPEG's inverse DCT and colour conversion) to a build flag, because 32-bit ARM
// chips once came with and without NEON. A compiler targeting one that has it says so with __ARM_NEON, which
// every 64-bit ARM target defines: Android phones, Apple silicon, Linux on ARM. A feature check rather than a
// list of platforms, so a leg without NEON keeps the C loops; stb_image_resize2 already makes the same check.
// About 1.6x faster on a 1196x822 JPEG on Apple silicon, with byte-identical output.
//
// Not under MSVC: its ARM64 leg builds on an x64 runner that can't run the tests, so nothing would check the
// NEON loops it compiled.
#if defined(__ARM_NEON) && !defined(_MSC_VER) && !defined(STBI_NO_SIMD)
#define STBI_NEON
#endif

#endif // SAKURA_IMAGE_STB_CONFIG_H
