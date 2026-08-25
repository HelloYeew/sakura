// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

// Covers the contract the managed side depends on and the bounds checks that stop a malformed header
// from turning into an out-of-bounds read. Pixel-level resampling quality is settled by the managed
// comparison harness, not here.

#include "../sakura_image.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int failures = 0;

#define CHECK(cond, ...)                                     \
    do {                                                     \
        if (!(cond)) {                                       \
            printf("FAIL %s:%d: ", __FILE__, __LINE__);      \
            printf(__VA_ARGS__);                             \
            printf("\n");                                    \
            failures++;                                      \
        }                                                    \
    } while (0)

// A 2x2 BMP, red / green / blue / white, bottom-up as BMP stores it. Small enough to inline, which
// keeps the test binary free of any file I/O -- STBI_NO_STDIO means the library could not read one
// anyway.
static const unsigned char bmp_2x2[] = {
    0x42, 0x4D, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
    0x28, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00,
    0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
    0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    // row 0 (bottom): blue, white
    0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00,
    // row 1 (top): red, green
    0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00
};

static void test_abi_version(void)
{
    CHECK(sakura_image_abi_version() == SAKURA_IMAGE_ABI_VERSION,
          "abi version %d, expected %d", sakura_image_abi_version(), SAKURA_IMAGE_ABI_VERSION);
}

static void test_info(void)
{
    int w = 0, h = 0;
    int rc = sakura_image_info(bmp_2x2, (int)sizeof(bmp_2x2), &w, &h);

    CHECK(rc == SAKURA_IMAGE_OK, "info returned %d", rc);
    CHECK(w == 2 && h == 2, "info reported %dx%d, expected 2x2", w, h);
}

static void test_info_rejects_garbage(void)
{
    const unsigned char garbage[] = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
    int w = 0, h = 0;

    CHECK(sakura_image_info(garbage, (int)sizeof(garbage), &w, &h) == SAKURA_IMAGE_ERROR,
          "garbage was accepted as a header");
    CHECK(sakura_image_info(NULL, 8, &w, &h) == SAKURA_IMAGE_INVALID, "NULL buffer was accepted");
    CHECK(sakura_image_info(bmp_2x2, 0, &w, &h) == SAKURA_IMAGE_INVALID, "zero length was accepted");
}

static void test_load_full_size_is_a_copy(void)
{
    unsigned char dst[2 * 2 * 4];
    memset(dst, 0xAA, sizeof(dst));

    int rc = sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), 0, 0, 2, 2, dst, 2, 2, (int)sizeof(dst));
    CHECK(rc == SAKURA_IMAGE_OK, "load returned %d", rc);

    // top-left is red, and alpha is filled even though the source has no alpha channel
    CHECK(dst[0] == 0xFF && dst[1] == 0x00 && dst[2] == 0x00 && dst[3] == 0xFF,
          "top-left was %02X%02X%02X%02X, expected FF0000FF", dst[0], dst[1], dst[2], dst[3]);
    // top-right is green
    CHECK(dst[4] == 0x00 && dst[5] == 0xFF && dst[6] == 0x00, "top-right was not green");
}

static void test_load_crops_without_resampling(void)
{
    unsigned char dst[1 * 1 * 4];
    memset(dst, 0xAA, sizeof(dst));

    // the bottom-right pixel alone, same size in as out, so this takes the memcpy path
    int rc = sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), 1, 1, 1, 1, dst, 1, 1, (int)sizeof(dst));
    CHECK(rc == SAKURA_IMAGE_OK, "cropped load returned %d", rc);
    CHECK(dst[0] == 0xFF && dst[1] == 0xFF && dst[2] == 0xFF, "bottom-right was not white");
}

static void test_load_resamples(void)
{
    unsigned char dst[1 * 1 * 4];
    memset(dst, 0xAA, sizeof(dst));

    int rc = sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), 0, 0, 2, 2, dst, 1, 1, (int)sizeof(dst));
    CHECK(rc == SAKURA_IMAGE_OK, "resampling load returned %d", rc);
    // averaging red, green, blue and white in linear light gives a mid grey; the exact value is the
    // resampler's business, but every channel has to land strictly between the extremes.
    CHECK(dst[0] > 0x00 && dst[0] < 0xFF, "red channel %02X was not blended", dst[0]);
    CHECK(dst[3] == 0xFF, "alpha %02X was not opaque", dst[3]);
}

static void test_load_rejects_bad_arguments(void)
{
    unsigned char dst[2 * 2 * 4];

    CHECK(sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), 0, 0, 2, 2, dst, 2, 2, 4) == SAKURA_IMAGE_INVALID,
          "a destination too small was accepted");
    CHECK(sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), 0, 0, 2, 2, NULL, 2, 2, (int)sizeof(dst)) == SAKURA_IMAGE_INVALID,
          "a NULL destination was accepted");
    CHECK(sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), 0, 0, 0, 2, dst, 2, 2, (int)sizeof(dst)) == SAKURA_IMAGE_INVALID,
          "a zero-width region was accepted");
}

static void test_load_rejects_region_outside_image(void)
{
    unsigned char dst[2 * 2 * 4];

    // the bounds check that stops a header/body disagreement from becoming an out-of-bounds read
    CHECK(sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), 1, 1, 2, 2, dst, 2, 2, (int)sizeof(dst)) == SAKURA_IMAGE_INVALID,
          "a region running past the right edge was accepted");
    CHECK(sakura_image_load(bmp_2x2, (int)sizeof(bmp_2x2), -1, 0, 2, 2, dst, 2, 2, (int)sizeof(dst)) == SAKURA_IMAGE_INVALID,
          "a negative origin was accepted");
}

static void test_load_rejects_garbage(void)
{
    const unsigned char garbage[] = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
    unsigned char dst[2 * 2 * 4];

    CHECK(sakura_image_load(garbage, (int)sizeof(garbage), 0, 0, 2, 2, dst, 2, 2, (int)sizeof(dst)) == SAKURA_IMAGE_ERROR,
          "garbage decoded successfully");
}

int main(void)
{
    test_abi_version();
    test_info();
    test_info_rejects_garbage();
    test_load_full_size_is_a_copy();
    test_load_crops_without_resampling();
    test_load_resamples();
    test_load_rejects_bad_arguments();
    test_load_rejects_region_outside_image();
    test_load_rejects_garbage();

    if (failures == 0)
    {
        printf("all sakura-image tests passed\n");
        return 0;
    }

    printf("%d sakura-image test(s) failed\n", failures);
    return 1;
}
