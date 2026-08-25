# sakura-image

A thin shim over [stb_image](https://github.com/nothings/stb) and stb_image_resize2, behind
`StbImageLoader` in the managed framework. Decodes an encoded image from a buffer, optionally crops a
region of it, and scales that region into a caller-supplied RGBA8 buffer.

## API

Three exports, declared in `sakura_image.h`:

| function | does |
|---|---|
| `sakura_image_abi_version()` | the ABI the library was built with; the managed side refuses a mismatch |
| `sakura_image_info()` | width and height from the header alone, no pixels decoded |
| `sakura_image_load()` | decode, crop to a region, scale into the caller's buffer |

`sakura_image_load` writes nothing on failure and returns a negative code — `SAKURA_IMAGE_ERROR` for
data the decoder rejected, `SAKURA_IMAGE_INVALID` for bad arguments, `SAKURA_IMAGE_NOMEM` for a failed
allocation. There is no output allocation to free: the caller owns the destination.

**The crop is a region, not a pre-cropped buffer.** stb decodes everything regardless, but passing
`stb_image_resize2` a sub-rectangle (a pointer offset plus the source stride) means the pixels a Fill
discards are never filtered, which is the expensive half.

## Stb fetch version

[nothings/stb](https://github.com/nothings/stb)

| file | version | fetched |
|---|---|---|
| `stb_image.h` | v2.30 | 2026-08-25, from `master` |
| `stb_image_resize2.h` | v2.18 | 2026-08-25, from `master` |

## Building

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build && ctest --test-dir build
```

macOS produces a universal `x86_64;arm64` dylib by default.

**Verify the exports on every RID before believing a package works.** The Windows DLLs in the audio
package once shipped with no export directory at all and nobody noticed until yuuki ran on Windows:

```bash
nm -gU build/libsakura-image.dylib          # macOS / Linux: expect exactly the three sakura_image_* symbols
dumpbin /exports sakura-image.dll           # Windows
```

## Shipping

The built library goes to `Sakura.Framework.NativeLibraries/runtimes/<rid>/native/` and reaches
consumers through the `Sakura.Framework.NativeLibraries` NuGet package. The framework takes that as a
`PackageReference`, so **a locally built dylib is not visible to a local test run until it is either
packaged or copied into the test output's `runtimes/<rid>/native/`.**
