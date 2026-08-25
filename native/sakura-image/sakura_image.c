// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#include "sakura_image.h"

#include <string.h>

#include "stb_config.h"

// Declarations only -- stb_impl.c holds the instantiation.
#include "vendor/stb_image.h"
#include "vendor/stb_image_resize2.h"

int sakura_image_abi_version(void)
{
    return SAKURA_IMAGE_ABI_VERSION;
}

const char *sakura_image_stb_version(void)
{
    return SAKURA_IMAGE_STB_IMAGE_VERSION;
}

const char *sakura_image_stb_resize_version(void)
{
    return SAKURA_IMAGE_STB_RESIZE_VERSION;
}

const char *sakura_image_formats(void)
{
    // Keyed on the STBI_ONLY_* defines from stb_config.h, which both translation units see, rather than
    // on STBI_NO_*: stb derives those from the ONLY_ set inside its STB_IMAGE_IMPLEMENTATION guard, so
    // they do not exist in this file and testing them here silently reported every format stb has.
    //
    // Each entry carries a leading separator and the first two characters are skipped, which is the
    // only way to build a comma-separated list in the preprocessor without knowing which entry is
    // first.
#if defined(STBI_ONLY_JPEG) || defined(STBI_ONLY_PNG) || defined(STBI_ONLY_BMP) || defined(STBI_ONLY_GIF) \
 || defined(STBI_ONLY_TGA) || defined(STBI_ONLY_PSD) || defined(STBI_ONLY_HDR) || defined(STBI_ONLY_PIC) \
 || defined(STBI_ONLY_PNM)
    static const char *const list = ""
#ifdef STBI_ONLY_JPEG
        ", JPEG"
#endif
#ifdef STBI_ONLY_PNG
        ", PNG"
#endif
#ifdef STBI_ONLY_BMP
        ", BMP"
#endif
#ifdef STBI_ONLY_GIF
        ", GIF"
#endif
#ifdef STBI_ONLY_TGA
        ", TGA"
#endif
#ifdef STBI_ONLY_PSD
        ", PSD"
#endif
#ifdef STBI_ONLY_HDR
        ", HDR"
#endif
#ifdef STBI_ONLY_PIC
        ", PIC"
#endif
#ifdef STBI_ONLY_PNM
        ", PNM"
#endif
        ;

    return list[0] == '\0' ? "none" : list + 2;
#else
    // No ONLY_ restriction, so stb compiled in everything it has.
    return "JPEG, PNG, BMP, GIF, TGA, PSD, HDR, PIC, PNM";
#endif
}

int sakura_image_info(const unsigned char *encoded, int length, int *width, int *height)
{
    if (encoded == NULL || length <= 0 || width == NULL || height == NULL)
        return SAKURA_IMAGE_INVALID;

    int channels;

    if (!stbi_info_from_memory(encoded, length, width, height, &channels))
        return SAKURA_IMAGE_ERROR;

    return SAKURA_IMAGE_OK;
}

int sakura_image_load(const unsigned char *encoded, int length,
                      int src_x, int src_y, int src_width, int src_height,
                      unsigned char *dst, int dst_width, int dst_height, int dst_length)
{
    if (encoded == NULL || length <= 0 || dst == NULL)
        return SAKURA_IMAGE_INVALID;

    if (src_width <= 0 || src_height <= 0 || dst_width <= 0 || dst_height <= 0)
        return SAKURA_IMAGE_INVALID;

    // (long long) rather than int: the product of two in-range dimensions still overflows a 32-bit int
    // at ~23000 square, which STBI_MAX_DIMENSIONS permits.
    long long needed = (long long)dst_width * dst_height * 4;

    if (needed > (long long)dst_length)
        return SAKURA_IMAGE_INVALID;

    int width, height, channels;
    // 4 forces RGBA8 out regardless of what the file holds, which is what ImageRawData documents.
    unsigned char *pixels = stbi_load_from_memory(encoded, length, &width, &height, &channels, 4);

    if (pixels == NULL)
        return SAKURA_IMAGE_ERROR;

    // The caller sized the region from sakura_image_info, so a mismatch means the header and the body
    // disagree. Refusing is the only safe answer: the offsets below would otherwise read out of bounds.
    if (src_x < 0 || src_y < 0 ||
        (long long)src_x + src_width > (long long)width ||
        (long long)src_y + src_height > (long long)height)
    {
        stbi_image_free(pixels);
        return SAKURA_IMAGE_INVALID;
    }

    int result = SAKURA_IMAGE_OK;

    if (src_width == dst_width && src_height == dst_height)
    {
        // Nothing to resample. Copy the region row by row; the fast path where the region is the whole
        // image is just the case where the row length equals the stride.
        size_t row_bytes = (size_t)dst_width * 4;

        for (int y = 0; y < dst_height; y++)
        {
            memcpy(dst + (size_t)y * row_bytes,
                   pixels + ((size_t)(src_y + y) * (size_t)width + (size_t)src_x) * 4,
                   row_bytes);
        }
    }
    else
    {
        // The crop is expressed as a pointer offset plus the full image's stride, so stb_image_resize2
        // filters only the region rather than the whole decode.
        const unsigned char *region = pixels + ((size_t)src_y * (size_t)width + (size_t)src_x) * 4;

        // STBIR_TYPE_UINT8_SRGB resamples in linear light. Measured against the full-resolution
        // originals, this preserves mean linear luminance to five decimal places where an sRGB-space
        // resize loses up to 4% of it on a heavily reduced cover. CATMULLROM rather than the
        // stbir_resize_uint8_srgb default of MITCHELL, because Catmull-Rom is what ImageSharp's default
        // Bicubic already is -- at matched filter and colour space the two resamplers agree to a max
        // channel delta of 1, so this pins sharpness to the incumbent and changes only the gamma
        // handling. See STBI_PLAN.md, "Phase 1 gate".
        void *resized = stbir_resize((const void *)region, src_width, src_height, width * 4,
                                     (void *)dst, dst_width, dst_height, dst_width * 4,
                                     STBIR_RGBA, STBIR_TYPE_UINT8_SRGB, STBIR_EDGE_CLAMP, STBIR_FILTER_CATMULLROM);

        if (resized == NULL)
            result = SAKURA_IMAGE_NOMEM;
    }

    stbi_image_free(pixels);
    return result;
}
