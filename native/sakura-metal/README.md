# sakura-metal

A thin C bridge over Apple's [Metal](https://developer.apple.com/metal/) framework. It exposes a
flat C ABI (`SakuraMetal.h`) over the Objective-C Metal API so a non-Objective-C host (Sakura in this case, can use it via P/Invoke)

It builds to `libsakura-metal.dylib` (macOS) and `sakura-metal.framework` (iOS). Apple platforms only.

## Note

This is **not** a general-purpose Metal wrapper. The C API is shaped by Sakura's rendering model:
fixed vertex-buffer binding slot, SPIRV-Cross `main0` entry-point names, blend modes matching
Sakura's `BlendingMode` enum, std140 uniform-buffer index conventions, and a YUV420P video-plane
layout. You can read it as a reference for "Metal behind a C ABI," but consuming it as-is means
adopting those conventions.

It is self-contained (one `.h`, one `.m`, no third-party dependencies beyond Apple frameworks)

## C API surface

Opaque handles only (`SakuraMetalDevice*`, `SakuraMetalPipeline*`, `SakuraMetalTexture*`); the host
holds pointers and never touches Objective-C objects directly. 

Short description:

- **Device + frame lifecycle** — `create`/`destroy`, `begin_frame`/`end_frame`, `resize`.
- **Pipelines** — `create_pipeline` from cross-compiled MSL, one variant per blend mode.
- **Draw** — `set_pipeline`, vertex/fragment uniform uploads, `draw_triangles` (small draws use
  `setVertexBytes`; large draws use a triple-buffered, semaphore-gated vertex ring).
- **Textures** — RGBA8 create/upload/upload-region (RGBA→BGRA swizzle), R8 video planes, render
  targets; `set_fragment_texture` (+ a wrap-forcing variant for video planes).
- **Offscreen render targets** — `create_render_target`, `begin_offscreen`/`end_offscreen` (a
  render-pass stack that emulates framebuffer nesting; the parent is reopened with a Load action).

See `SakuraMetal.h` for the full, commented declarations.

## Build

Requires CMake >= 3.22 and Xcode (for the Metal/QuartzCore/Foundation frameworks).

### macOS (`libsakura-metal.dylib`)

```sh
cd native/sakura-metal
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
```

This produces `build/libsakura-metal.dylib`.

### iOS (`sakura-metal.framework`)

Configure with the iOS toolchain (`-DCMAKE_SYSTEM_NAME=iOS`); the build emits a `.framework`, which
the (future) CI workflow assembles into an `xcframework`.

> The `build/` directory is machine-specific CMake output (it bakes in absolute paths) and is
> git-ignored — never commit it.

## Where the dylib goes

The .NET side loads the library by name (`DllImport("libsakura-metal")`), so the built artifact must
sit where the framework's native loader finds it, alongside the other natives:

```
Sakura.Framework.NativeLibraries/runtimes/osx/native/libsakura-metal.dylib      # macOS
Sakura.Framework.NativeLibraries/runtimes/ios/native/sakura-metal.xcframework   # iOS
```

## Relationship to the C# side

The matching P/Invoke binding lives in the .NET project at`SakuraMetalNative.cs`. When you change a function's signature here, update that file to match.
