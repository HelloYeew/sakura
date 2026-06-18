// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#ifndef SAKURA_METAL_H
#define SAKURA_METAL_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct SakuraMetalDevice SakuraMetalDevice;
typedef struct SakuraMetalPipeline SakuraMetalPipeline;
typedef struct SakuraMetalTexture SakuraMetalTexture;

typedef struct SakuraMetalVertexAttribute
{
    int attributeIndex; // layout(location = N) in the shader
    int componentCount; // 1..4
    int offset;         // byte offset within the vertex struct
} SakuraMetalVertexAttribute;

// Diagnostic info about the Metal device, filled by sakura_metal_get_info. Mirrors the GL backend's
// startup logging (device name / API tier / capabilities). `name` is written into the caller-provided
// buffer (UTF-8, null-terminated, truncated to nameCapacity); the rest are scalar facts.
typedef struct SakuraMetalInfo
{
    int    maxThreadsPerThreadgroup; // device.maxThreadsPerThreadgroup.width
    int    hasUnifiedMemory;         // 1 if the GPU shares memory with the CPU (Apple Silicon), else 0
    int    supportsFamilyApple;      // highest supported MTLGPUFamilyApple<n> (0 if none)
    int    supportsFamilyMac;        // highest supported MTLGPUFamilyMac<n> (0 if none)
    unsigned long long recommendedMaxWorkingSetSize; // bytes; 0 if unavailable
} SakuraMetalInfo;

// Device + layer

// Creates a Metal device wrapper bound to the given CAMetalLayer pointer (from SDL3 via IMetalGraphicsSurface.MetalLayer).
// Returns NULL on failure.
SakuraMetalDevice* sakura_metal_create(void* caMetalLayer);
void sakura_metal_destroy(SakuraMetalDevice* device);

// Fills `info` and writes the device name into `nameBuffer` (UTF-8, null-terminated). Used for
// startup diagnostics. Safe to call any time after create. `nameBuffer` may be NULL to skip the name.
void sakura_metal_get_info(SakuraMetalDevice* device, SakuraMetalInfo* info, char* nameBuffer, int nameCapacity);

// Frame lifecycle

// begin frame: acquires the next drawable, sets up a command buffer + render pass that clears
// the framebuffer to the given (non-premultiplied) RGBA colour in [0,1]
void sakura_metal_begin_frame(SakuraMetalDevice* device, float r, float g, float b, float a);

// end frame: closes the render encoder, presents the drawable, and commits the command buffer.
void sakura_metal_end_frame(SakuraMetalDevice* device);

// handles a backbuffer resize, width/height are in physical pixels. contentsScale is the backing
// scale factor (physical / logical, e.g. 2.0 on Retina) so Core Animation composites the layer at
// native resolution instead of upscaling.
void sakura_metal_resize(SakuraMetalDevice* device, int width, int height, float contentsScale);

// Pipeline (compiled shader pair)

// Compiles the given MSL vertex+fragment source into a render pipeline state, using the supplied
// vertex attribute layout (interleaved, single buffer of stride `vertexStride`). Entry points are
// the SPIRV-Cross defaults ("main0"). `blendMode` selects the colour-attachment blend state (matches
// the C# BlendingMode enum: 0=Alpha, 1=Additive, 2=Opaque, 3=Multiply, 4=Screen, 5=Premultiplied) —
// Metal bakes blend state into the pipeline, so one pipeline is created per (shader, blend) pair.
// Returns NULL on failure.
SakuraMetalPipeline* sakura_metal_create_pipeline(
    SakuraMetalDevice* device,
    const char* vertexMsl,
    const char* fragmentMsl,
    const SakuraMetalVertexAttribute* attributes,
    int attributeCount,
    int vertexStride,
    int blendMode);

void sakura_metal_destroy_pipeline(SakuraMetalPipeline* pipeline);

// Draw (within a begin_frame / end_frame pair)

// sets the pipeline for subsequent draws this frame.
void sakura_metal_set_pipeline(SakuraMetalDevice* device, SakuraMetalPipeline* pipeline);

// uploads `length` bytes to the vertex-stage buffer at `index` (used for uniform blocks like the
// projection matrix, index matches the MSL buffer binding SPIRV-Cross assigned).
void sakura_metal_set_vertex_uniform(SakuraMetalDevice* device, const void* data, int length, int index);

// uploads `length` bytes to the fragment-stage buffer at `index` (used for uniform blocks like the
// MaskBlock; index matches the MSL buffer binding SPIRV-Cross assigned — fragment [[buffer(1)]] for
// the main shader's MaskBlock).
void sakura_metal_set_fragment_uniform(SakuraMetalDevice* device, const void* data, int length, int index);

// draws `vertexCount` vertices from `data` (interleaved vertex structs) as a triangle list
// vertex buffer is bound at buffer index 0 (SPIRV-Cross's default for stage_in vertex data)
void sakura_metal_draw_triangles(SakuraMetalDevice* device, const void* data, int vertexCount, int vertexStride);

// offscreen render targets (framebuffers)

// creates a renderable + sampleable texture (BGRA8Unorm_sRGB, no mipmaps, clamp-to-edge sampling)
// for use as an offscreen render target. sampled with a clamp sampler (see set_fragment_texture),
// which the separable blur relies on. returns NULL on failure, destroy with destroy_texture.
SakuraMetalTexture* sakura_metal_create_render_target(SakuraMetalDevice* device, int width, int height);

// begins rendering into an offscreen render target, ends the current render command encoder and
// starts a new one targeting `texture`, clearing it to the given RGBA colour. the previous target
// (the drawable, or an outer offscreen target) is pushed on an internal stack and restored by the
// matching end_offscreen. each offscreen pass uses its own command buffer, committed at end.
void sakura_metal_begin_offscreen(SakuraMetalDevice* device, SakuraMetalTexture* texture, float r, float g, float b, float a);

// ends the current offscreen pass and restores the previous render target, reopening its encoder
// with a LOAD action so its existing contents are preserved (not cleared). Must be balanced with
// begin_offscreen.
void sakura_metal_end_offscreen(SakuraMetalDevice* device);

// Note: blend mode is baked into the pipeline (see the blendMode arg on create_pipeline), so there is
// no separate encoder-level blend call, the renderer binds the matching pipeline variant.

// Texture

// creates an RGBA8 (BGRA8Unorm_sRGB) texture of the given size. returns NULL on failure.
SakuraMetalTexture* sakura_metal_create_texture(SakuraMetalDevice* device, int width, int height);

// uploads `width*height*4` bytes of RGBA8 pixel data (whole texture).
void sakura_metal_upload_texture(SakuraMetalTexture* texture, const void* data, int width, int height);

// uploads `width*height*4` bytes of RGBA8 pixel data into the sub-region at (x, y) used by the
// glyph/sprite atlas to blit individual entries into a shared page.
void sakura_metal_upload_texture_region(SakuraMetalTexture* texture, int x, int y, int width, int height, const void* data);

void sakura_metal_destroy_texture(SakuraMetalTexture* texture);

// video planes (single-channel R8 textures)

// creates a single-channel R8Unorm texture for one YUV plane (linear, clamp-to-edge sampling, no
// mips, NOT sRGB — the samples are raw luma/chroma, not colour). returns NULL on failure.
SakuraMetalTexture* sakura_metal_create_plane_texture(SakuraMetalDevice* device, int width, int height);

// uploads single-channel R8 pixel data into a plane texture. `bytesPerRow` is the source stride
// (FFmpeg's linesize, which may exceed width due to padding); pass width for tightly-packed data.
// no swizzle (single channel), replaces the whole texture.
void sakura_metal_upload_plane(SakuraMetalTexture* texture, const void* data, int width, int height, int bytesPerRow);

// binds the texture (and a default linear sampler) to the given fragment texture/sampler slot
// for subsequent draws this frame. the main shader's u_Textures[] is at [[texture(0)]]/[[sampler(0)]].
// the sampler is chosen automatically (clamp for render targets/video planes, repeat otherwise).
void sakura_metal_set_fragment_texture(SakuraMetalDevice* device, SakuraMetalTexture* texture, int slot);

// like set_fragment_texture, but forces the wrap mode: repeat != 0 -> repeating sampler, else clamp.
// Used by video planes whose wrap depends on the sprite's fill mode (Tile -> repeat) rather than on a
// fixed per-texture choice.
void sakura_metal_set_fragment_texture_wrap(SakuraMetalDevice* device, SakuraMetalTexture* texture, int slot, int repeat);

#ifdef __cplusplus
}
#endif

#endif // SAKURA_METAL_H
