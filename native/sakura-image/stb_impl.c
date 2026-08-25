// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

// The vendored stb headers instantiated, and nothing else. Separated from sakura_image.c so CMake can
// relax the warning flags for third-party code alone, leaving this project's own C held to
// -Wall -Wextra -Wpedantic.

#include "stb_config.h"

#define STB_IMAGE_IMPLEMENTATION
#include "vendor/stb_image.h"

#define STB_IMAGE_RESIZE_IMPLEMENTATION
#include "vendor/stb_image_resize2.h"
