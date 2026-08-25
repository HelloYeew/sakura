// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#ifndef SAKURA_IMAGE_H
#define SAKURA_IMAGE_H

#ifdef __cplusplus
extern "C" {
#endif

// Bumped whenever the signatures or the meaning of the codes below change. The managed side refuses a
// library that disagrees rather than trusting it to have the same contract.
#define SAKURA_IMAGE_ABI_VERSION 2

// The versions of the vendored headers, as declared in their own leading comments. Exported so the
// managed side can report which stb is actually loaded rather than which one the repo's README says was
// vendored -- those answers differ the moment a stale native is on the machine. Update both when
// vendor/ is synced.
#define SAKURA_IMAGE_STB_IMAGE_VERSION "2.30"
#define SAKURA_IMAGE_STB_RESIZE_VERSION "2.18"

#define SAKURA_IMAGE_OK        0
#define SAKURA_IMAGE_ERROR    -1  // the decoder rejected the data: unsupported format, or corrupt
#define SAKURA_IMAGE_INVALID  -2  // bad arguments, e.g. a source region outside the decoded image
#define SAKURA_IMAGE_NOMEM    -3  // an allocation failed

// The vendored stb headers declare their functions extern, not static, so unlike sakura-audio this
// library's non-API symbols are not already private. Default visibility is hidden (see CMakeLists) and
// only the three below are re-exposed, which keeps ~50 stbi_*/stbir_* symbols out of the dynamic
// symbol table. On Windows CMake's WINDOWS_EXPORT_ALL_SYMBOLS still does the exporting, so this macro
// is deliberately empty there rather than __declspec(dllexport) -- two mechanisms disagreeing about
// what is exported is exactly the failure AUDIO_SDL.md records.
#if defined(_WIN32)
#define SAKURA_IMAGE_API
#else
#define SAKURA_IMAGE_API __attribute__((visibility("default")))
#endif

SAKURA_IMAGE_API int sakura_image_abi_version(void);

/// The vendored stb_image version, e.g. "2.30". Statically allocated; the caller must not free it.
SAKURA_IMAGE_API const char *sakura_image_stb_version(void);

/// The vendored stb_image_resize2 version, e.g. "2.18". Statically allocated; must not be freed.
SAKURA_IMAGE_API const char *sakura_image_stb_resize_version(void);

/// The formats this build can decode, e.g. "JPEG, PNG, BMP, GIF". Derived from the STBI_ONLY_* defines
/// rather than written by hand, so it cannot drift from what was actually compiled in. Statically
/// allocated; must not be freed.
SAKURA_IMAGE_API const char *sakura_image_formats(void);

/// Reads dimensions from the header alone, without decoding any pixels.
/// Returns SAKURA_IMAGE_OK, or a negative code.
SAKURA_IMAGE_API int sakura_image_info(const unsigned char *encoded, int length, int *width, int *height);

/// Decodes `encoded`, takes the [src_x, src_y, src_width, src_height] region of the result, and scales
/// that region into `dst` as RGBA8 at dst_width x dst_height.
///
/// `dst_length` must be at least dst_width * dst_height * 4, and is checked rather than trusted.
/// Passing the full decoded rectangle with dst_width/dst_height equal to it copies without resampling.
///
/// The region is how a Fill crop is served: stb decodes everything regardless, but resampling only the
/// centre band means the discarded pixels are never filtered, which is the expensive half.
///
/// Returns SAKURA_IMAGE_OK, or a negative code. On failure `dst` is left untouched.
SAKURA_IMAGE_API int sakura_image_load(const unsigned char *encoded, int length,
                      int src_x, int src_y, int src_width, int src_height,
                      unsigned char *dst, int dst_width, int dst_height, int dst_length);

#ifdef __cplusplus
}
#endif

#endif // SAKURA_IMAGE_H
