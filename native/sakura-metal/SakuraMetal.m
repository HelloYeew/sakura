// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#import "SakuraMetal.h"

#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#import <TargetConditionals.h>
#include <string.h> // strlcpy
#if TARGET_OS_IPHONE
#import <UIKit/UIKit.h>
#else
#import <AppKit/AppKit.h>
#endif

// Buffer index for the interleaved vertex data fed to [[stage_in]]. SPIRV-Cross places UBOs at
// low buffer indices (the projection block is [[buffer(0)]] on the vertex stage), so the geometry
// buffer must avoid those. Metal guarantees at least 31 buffer slots; 30 is the conventional high
// slot for vertex data and won't collide with the cross-compiled UBO bindings.
static const NSUInteger SAKURA_METAL_VERTEX_BUFFER_INDEX = 30;

// Max nesting depth of offscreen render targets. BufferedContainer nests at most a few deep
// (content buffer → blur ping-pong → grayscale); 8 is comfortable headroom.
#define SAKURA_METAL_MAX_TARGETS 8

// Number of frames the CPU may record ahead of the GPU. The large-draw vertex buffer is a ring of
// this many buffers, gated by a semaphore, so the CPU never writes a buffer the GPU is still reading
// (the fix for the old single-buffer tearing hazard). 3 is the conventional choice (double-buffering
// can stall; >3 adds latency for no gain).
#define SAKURA_METAL_MAX_FRAMES_IN_FLIGHT 3

struct SakuraMetalPipeline
{
    __unsafe_unretained id<MTLRenderPipelineState> state;
};

struct SakuraMetalTexture
{
    __unsafe_unretained id<MTLTexture> texture;
    __unsafe_unretained id<MTLCommandQueue> queue; // for generating mipmaps via a one-shot blit
    BOOL hasMips;
    BOOL isRenderTarget; // sampled with the clamp sampler (blur needs clamp-to-edge), not repeat
    BOOL isPlane;        // single-channel R8 YUV plane; also sampled clamp-to-edge
};

// One entry on the render-target stack: the texture being rendered into (nil = the drawable) plus
// the command buffer + encoder recording into it. Each target gets its own command buffer so the
// serial queue executes them in descent/ascent order (parent draws → child pass → parent reload).
typedef struct SakuraMetalTarget
{
    __unsafe_unretained id<MTLTexture> texture; // nil = drawable
    __unsafe_unretained id<MTLCommandBuffer> commandBuffer;
    __unsafe_unretained id<MTLRenderCommandEncoder> encoder;
} SakuraMetalTarget;

struct SakuraMetalDevice
{
    // __unsafe_unretained: this is a plain C struct (not ARC-managed), so we hold these refs without
    // ARC ownership and balance them manually. We keep strong-equivalent lifetime by retaining in
    // create and releasing in destroy via CFBridging.
    __unsafe_unretained id<MTLDevice> device;
    __unsafe_unretained id<MTLCommandQueue> queue;
    __unsafe_unretained CAMetalLayer* layer;
    __unsafe_unretained id<MTLSamplerState> sampler;      // linear/mip/repeat — normal textures
    __unsafe_unretained id<MTLSamplerState> clampSampler; // linear/clamp-to-edge — render targets

    // Per-frame transient objects (valid only between begin_frame and end_frame).
    __unsafe_unretained id<CAMetalDrawable> drawable;

    // Render-target stack. targets[0] is the drawable (set up in begin_frame); begin/end_offscreen
    // push/pop offscreen passes. `encoder`/`commandBuffer` always mirror the top of the stack (the
    // active target) so the existing draw functions need no changes.
    SakuraMetalTarget targets[SAKURA_METAL_MAX_TARGETS];
    int targetDepth; // index of the active target; -1 when no frame is open
    __unsafe_unretained id<MTLCommandBuffer>       commandBuffer; // == targets[targetDepth].commandBuffer
    __unsafe_unretained id<MTLRenderCommandEncoder> encoder;       // == targets[targetDepth].encoder

    // Vertex buffers for draws above the 4KB setVertexBytes limit. The framework issues many draws per
    // frame; each appends at vertexBufferOffset (bump allocation) so concurrently-recorded draws don't
    // clobber each other (the GPU reads the buffer at command-execution time).
    //
    // TRIPLE-BUFFERED RING: there are MAX_FRAMES_IN_FLIGHT separate buffers; frame N uses
    // ringBuffers[N % count]. A counting semaphore (initialised to that count) is waited in begin_frame
    // and signalled when the frame's final command buffer completes, so the CPU can be at most that
    // many frames ahead of the GPU and NEVER writes a buffer the GPU is still reading. This removes the
    // old single-buffer frame-to-frame tearing hazard.
    //
    // Within a frame the active buffer never reallocates (that would strand already-recorded draws on a
    // freed buffer — a use-after-free). It is grown only at the START of a frame, sized to the previous
    // frame's peak; a draw that would overflow the current frame's buffer is skipped, and the recorded
    // peak ensures next time round the ring the buffer is large enough.
    __unsafe_unretained id<MTLBuffer> ringBuffers[SAKURA_METAL_MAX_FRAMES_IN_FLIGHT];
    NSUInteger ringCapacity[SAKURA_METAL_MAX_FRAMES_IN_FLIGHT]; // allocated size of each ring buffer
    __unsafe_unretained dispatch_semaphore_t frameSemaphore; // limits CPU to MAX_FRAMES_IN_FLIGHT ahead of GPU
    BOOL semaphoreWaitedThisFrame;             // did begin_frame take the semaphore? (balance the signal)
    int frameIndex;                            // increments per frame; ring slot = frameIndex % count

    __unsafe_unretained id<MTLBuffer> vertexBuffer; // == ringBuffers[frameIndex % count] for this frame
    NSUInteger vertexBufferCapacity; // == ringCapacity[...] for the active buffer
    NSUInteger vertexBufferOffset;   // bump pointer within the current frame
    NSUInteger vertexBytesThisFrame; // total large-draw bytes requested this frame (peak tracking)
    NSUInteger vertexBytesLastFrame; // previous frame's peak, used to size the buffer in begin_frame
};

SakuraMetalDevice* sakura_metal_create(void* caMetalLayer)
{
    if (caMetalLayer == NULL)
        return NULL;

    CAMetalLayer* layer = (__bridge CAMetalLayer*)caMetalLayer;

    id<MTLDevice> device = MTLCreateSystemDefaultDevice();
    if (device == nil)
        return NULL;

    id<MTLCommandQueue> queue = [device newCommandQueue];
    if (queue == nil)
        return NULL;

    layer.device = device;
    // sRGB pixel format so the framebuffer encodes linear -> sRGB on write, matching the GL path's
    // GL_FRAMEBUFFER_SRGB behaviour (see METAL.md notes).
    layer.pixelFormat = MTLPixelFormatBGRA8Unorm_sRGB;
    layer.framebufferOnly = YES;

    // Set the backing scale + physical drawable size up front so the VERY FIRST frame renders at
    // native (Retina) resolution. Without this the layer defaults to contentsScale 1.0 and a
    // logical-sized drawable, so early frames (and any glyphs/sprites rasterised then) come out soft.
    // The C# resize path later keeps these in sync, but it runs after the first frames.
    CGFloat backingScale = 1.0;
#if !TARGET_OS_IPHONE
    if (NSScreen.mainScreen != nil)
        backingScale = NSScreen.mainScreen.backingScaleFactor;
#else
    backingScale = UIScreen.mainScreen.nativeScale;
#endif
    if (backingScale > 0.0)
    {
        layer.contentsScale = backingScale;
        CGSize bounds = layer.bounds.size;
        if (bounds.width > 0.0 && bounds.height > 0.0)
            layer.drawableSize = CGSizeMake(bounds.width * backingScale, bounds.height * backingScale);
    }

    // Default linear, mip-filtered, repeat-wrapped sampler shared by all fragment textures.
    MTLSamplerDescriptor* sd = [[MTLSamplerDescriptor alloc] init];
    sd.minFilter = MTLSamplerMinMagFilterLinear;
    sd.magFilter = MTLSamplerMinMagFilterLinear;
    // Trilinear mip sampling, matching GL's LinearMipmapLinear. Without this, minified textures
    // (e.g. small text in the atlas) alias badly.
    sd.mipFilter = MTLSamplerMipFilterLinear;
    // Repeat wrap matches GLTexture's default and is required for FillMode.Tile, which sets UVs > 1
    // and relies on the sampler to repeat. ClampToEdge would smear the edge texels into streaks.
    sd.sAddressMode = MTLSamplerAddressModeRepeat;
    sd.tAddressMode = MTLSamplerAddressModeRepeat;
    id<MTLSamplerState> sampler = [device newSamplerStateWithDescriptor:sd];

    // Clamp-to-edge sampler for offscreen render targets. The separable Gaussian blur samples beyond
    // the buffer edges and relies on edge-clamping (matching GL render targets' ClampToEdge wrap);
    // repeat would wrap the opposite edge in. No mip filtering: render targets have a single level.
    MTLSamplerDescriptor* csd = [[MTLSamplerDescriptor alloc] init];
    csd.minFilter = MTLSamplerMinMagFilterLinear;
    csd.magFilter = MTLSamplerMinMagFilterLinear;
    csd.mipFilter = MTLSamplerMipFilterNotMipmapped;
    csd.sAddressMode = MTLSamplerAddressModeClampToEdge;
    csd.tAddressMode = MTLSamplerAddressModeClampToEdge;
    id<MTLSamplerState> clampSampler = [device newSamplerStateWithDescriptor:csd];

    SakuraMetalDevice* d = (SakuraMetalDevice*)calloc(1, sizeof(SakuraMetalDevice));
    if (d == NULL)
        return NULL;

    // The struct fields are __unsafe_unretained (plain C struct, not ARC-managed), so we take a
    // manual +1 ref with CFRetain and balance it with CFRelease in destroy. CFRetain returns the
    // same object so no CF<->ObjC cast is needed.
    d->device = device;  CFRetain((__bridge CFTypeRef)device);
    d->queue = queue; CFRetain((__bridge CFTypeRef)queue);
    d->layer = layer; CFRetain((__bridge CFTypeRef)layer);
    if (sampler) {
        d->sampler = sampler;
        CFRetain((__bridge CFTypeRef)sampler);
    }
    if (clampSampler) {
        d->clampSampler = clampSampler;
        CFRetain((__bridge CFTypeRef)clampSampler);
    }
    d->targetDepth = -1;

    // Counting semaphore for the vertex-buffer ring: allows up to MAX_FRAMES_IN_FLIGHT frames to be
    // recorded before the CPU must wait for the GPU to finish the oldest one. The field is
    // __unsafe_unretained (plain C struct), so take a manual +1 and balance it in destroy.
    dispatch_semaphore_t sem = dispatch_semaphore_create(SAKURA_METAL_MAX_FRAMES_IN_FLIGHT);
    d->frameSemaphore = sem; CFRetain((__bridge CFTypeRef)sem);
    d->frameIndex = 0;

    return d;
}

void sakura_metal_destroy(SakuraMetalDevice* device)
{
    if (device == NULL)
        return;

    // If a frame was begun but never ended (the CPU took a semaphore slot that no completion handler
    // will ever signal), hand that slot back so the drain below can't deadlock.
    if (device->semaphoreWaitedThisFrame && device->frameSemaphore != nil)
    {
        dispatch_semaphore_signal(device->frameSemaphore);
        device->semaphoreWaitedThisFrame = NO;
    }

    // Drain any in-flight frames before tearing down, so no pending GPU completion handler reads a
    // freed ring buffer. Re-acquiring all MAX_FRAMES_IN_FLIGHT slots blocks until every recorded frame
    // has completed (each completion handler releases one slot). The blocks retain the semaphore
    // themselves, so it stays alive until the last handler runs even though we release our ref below.
    if (device->frameSemaphore != nil)
    {
        for (int i = 0; i < SAKURA_METAL_MAX_FRAMES_IN_FLIGHT; i++)
            dispatch_semaphore_wait(device->frameSemaphore, DISPATCH_TIME_FOREVER);
    }

    if (device->device) CFRelease((__bridge CFTypeRef)device->device);
    if (device->queue) CFRelease((__bridge CFTypeRef)device->queue);
    if (device->layer) CFRelease((__bridge CFTypeRef)device->layer);
    if (device->sampler) CFRelease((__bridge CFTypeRef)device->sampler);
    if (device->clampSampler) CFRelease((__bridge CFTypeRef)device->clampSampler);

    // Release the ring buffers (the +1 each from their last grow in begin_frame). vertexBuffer is just
    // an alias into this array for the active frame — not separately retained — so it's not released.
    for (int i = 0; i < SAKURA_METAL_MAX_FRAMES_IN_FLIGHT; i++)
        if (device->ringBuffers[i])
            CFRelease((__bridge CFTypeRef)device->ringBuffers[i]);

    if (device->frameSemaphore)
        CFRelease((__bridge CFTypeRef)device->frameSemaphore);

    free(device);
}

void sakura_metal_get_info(SakuraMetalDevice* device, SakuraMetalInfo* info, char* nameBuffer, int nameCapacity)
{
    if (device == NULL || device->device == nil || info == NULL)
        return;

    id<MTLDevice> mtl = device->device;

    info->maxThreadsPerThreadgroup = (int)mtl.maxThreadsPerThreadgroup.width;
    info->hasUnifiedMemory = mtl.hasUnifiedMemory ? 1 : 0;
    info->recommendedMaxWorkingSetSize = (unsigned long long)mtl.recommendedMaxWorkingSetSize;

    // Highest supported GPU family in each lineage. supportsFamily: is macOS 10.15+ / iOS 13+ (our
    // deployment targets). The newest family enum constants only exist in newer SDKs, so the Apple8/9
    // probes are compiled in only when the build SDK defines them (older SDKs simply top out lower).
    // We probe from high to low and record the top one that's supported.
    int apple = 0;
#if (defined(__MAC_OS_X_VERSION_MAX_ALLOWED) && __MAC_OS_X_VERSION_MAX_ALLOWED >= 140000) || \
    (defined(__IPHONE_OS_VERSION_MAX_ALLOWED) && __IPHONE_OS_VERSION_MAX_ALLOWED >= 170000)
    if ([mtl supportsFamily:MTLGPUFamilyApple9]) apple = 9;
    else
#endif
#if (defined(__MAC_OS_X_VERSION_MAX_ALLOWED) && __MAC_OS_X_VERSION_MAX_ALLOWED >= 130000) || \
    (defined(__IPHONE_OS_VERSION_MAX_ALLOWED) && __IPHONE_OS_VERSION_MAX_ALLOWED >= 160000)
    if ([mtl supportsFamily:MTLGPUFamilyApple8]) apple = 8;
    else
#endif
    if ([mtl supportsFamily:MTLGPUFamilyApple7]) apple = 7;
    else if ([mtl supportsFamily:MTLGPUFamilyApple6]) apple = 6;
    else if ([mtl supportsFamily:MTLGPUFamilyApple5]) apple = 5;
    else if ([mtl supportsFamily:MTLGPUFamilyApple4]) apple = 4;
    else if ([mtl supportsFamily:MTLGPUFamilyApple3]) apple = 3;
    else if ([mtl supportsFamily:MTLGPUFamilyApple2]) apple = 2;
    else if ([mtl supportsFamily:MTLGPUFamilyApple1]) apple = 1;
    info->supportsFamilyApple = apple;

    // Only Mac2 is probed: MTLGPUFamilyMac1 is deprecated (every Mac1-capable GPU also reports Mac2,
    // per Apple's deprecation note), so probing Mac1 would add nothing but a deprecation warning.
    info->supportsFamilyMac = [mtl supportsFamily:MTLGPUFamilyMac2] ? 2 : 0;

    if (nameBuffer != NULL && nameCapacity > 0)
    {
        const char* name = mtl.name.UTF8String;
        if (name == NULL) name = "";
        // strlcpy null-terminates and never overflows the buffer.
        strlcpy(nameBuffer, name, (size_t)nameCapacity);
    }
}

void sakura_metal_begin_frame(SakuraMetalDevice* device, float r, float g, float b, float a)
{
    if (device == NULL || device->layer == nil)
        return;

    // Throttle the CPU to at most MAX_FRAMES_IN_FLIGHT frames ahead of the GPU before touching this
    // frame's ring buffer. The matching signal fires from the frame's final command-buffer completion
    // handler (end_frame). Taken here so it also covers the no-drawable early-out below (we signal it
    // straight back in that case, so the count never leaks).
    device->semaphoreWaitedThisFrame = NO;
    if (device->frameSemaphore != nil)
    {
        dispatch_semaphore_wait(device->frameSemaphore, DISPATCH_TIME_FOREVER);
        device->semaphoreWaitedThisFrame = YES;
    }

    id<CAMetalDrawable> drawable = [device->layer nextDrawable];
    if (drawable == nil)
    {
        // No drawable available this frame (e.g. occluded); skip. Hand the semaphore slot back since no
        // command buffer will complete to signal it.
        if (device->semaphoreWaitedThisFrame)
        {
            dispatch_semaphore_signal(device->frameSemaphore);
            device->semaphoreWaitedThisFrame = NO;
        }
        return;
    }

    id<MTLCommandBuffer> commandBuffer = [device->queue commandBuffer];

    MTLRenderPassDescriptor* pass = [MTLRenderPassDescriptor renderPassDescriptor];
    pass.colorAttachments[0].texture = drawable.texture;
    pass.colorAttachments[0].loadAction = MTLLoadActionClear;
    pass.colorAttachments[0].storeAction = MTLStoreActionStore;
    pass.colorAttachments[0].clearColor = MTLClearColorMake(r, g, b, a);

    id<MTLRenderCommandEncoder> encoder = [commandBuffer renderCommandEncoderWithDescriptor:pass];

    // Hold the drawable for the whole frame (released in end_frame).
    device->drawable = drawable;
    CFRetain((__bridge CFTypeRef)drawable);

    // Base of the render-target stack: the drawable target. Retains balanced in end_frame /
    // end_offscreen as targets are popped.
    device->targetDepth = 0;
    device->targets[0].texture = nil; // drawable
    device->targets[0].commandBuffer = commandBuffer;
    CFRetain((__bridge CFTypeRef)commandBuffer);
    device->targets[0].encoder = encoder;
    CFRetain((__bridge CFTypeRef)encoder);
    device->commandBuffer = commandBuffer;
    device->encoder = encoder;

    // Select this frame's ring slot. The semaphore guarantees the GPU has finished reading this slot
    // (its frame completed), so it's safe to overwrite from the CPU now.
    int slot = device->frameIndex % SAKURA_METAL_MAX_FRAMES_IN_FLIGHT;

    // Start this frame's vertex allocations from the top of the selected buffer.
    device->vertexBufferOffset = 0;
    device->vertexBytesThisFrame = 0;

    // Grow THIS slot's buffer ONCE here (never mid-frame) to fit the previous frame's peak usage, so no
    // draw recorded this frame sees a reallocation. Headroom (1.5x) absorbs frame-to-frame growth. Each
    // ring slot grows independently (a slot only reallocates when it's the active one and too small).
    NSUInteger needed = device->vertexBytesLastFrame + (device->vertexBytesLastFrame / 2);
    if (needed > 0 && device->ringCapacity[slot] < needed)
    {
        id<MTLBuffer> newBuffer = [device->device newBufferWithLength:needed
                                                             options:MTLResourceStorageModeShared];
        if (newBuffer != nil)
        {
            if (device->ringBuffers[slot])
                CFRelease((__bridge CFTypeRef)device->ringBuffers[slot]);

            device->ringBuffers[slot] = newBuffer; CFRetain((__bridge CFTypeRef)newBuffer);
            device->ringCapacity[slot] = needed;
        }
    }

    // Point the active-buffer aliases at this frame's slot (used by draw_triangles).
    device->vertexBuffer         = device->ringBuffers[slot];
    device->vertexBufferCapacity = device->ringCapacity[slot];
}

void sakura_metal_end_frame(SakuraMetalDevice* device)
{
    if (device == NULL || device->targetDepth < 0)
        return;

    // Defensive: if the C# side left offscreen passes open (it shouldn't), close them so we end on the
    // drawable target and don't leak encoders/command buffers.
    while (device->targetDepth > 0)
        sakura_metal_end_offscreen(device);

    SakuraMetalTarget* top = &device->targets[0];

    if (top->encoder)
        [top->encoder endEncoding];

    if (device->drawable && top->commandBuffer)
        [top->commandBuffer presentDrawable:device->drawable];

    // Signal the frame semaphore when this frame's FINAL command buffer (the drawable's) completes on
    // the GPU — at which point the GPU is done reading this frame's ring buffer, freeing the slot for
    // reuse MAX_FRAMES_IN_FLIGHT frames later. Captured locally (not via the device pointer) so the
    // handler is safe even if the handler runs after teardown begins; the semaphore itself is kept
    // alive by destroy's drain (it waits for all in-flight frames before releasing). Added before
    // commit, as required.
    if (top->commandBuffer && device->semaphoreWaitedThisFrame && device->frameSemaphore != nil)
    {
        dispatch_semaphore_t sem = device->frameSemaphore;
        [top->commandBuffer addCompletedHandler:^(id<MTLCommandBuffer> _Nonnull cb) {
            dispatch_semaphore_signal(sem);
        }];
        device->semaphoreWaitedThisFrame = NO;
    }

    if (top->commandBuffer)
        [top->commandBuffer commit];

    // Release the drawable target's objects (balances the CFRetains in begin_frame / the last reopen).
    if (top->encoder) {
        CFRelease((__bridge CFTypeRef)top->encoder);
        top->encoder = nil;
    }
    if (top->commandBuffer) {
        CFRelease((__bridge CFTypeRef)top->commandBuffer);
        top->commandBuffer = nil;
    }
    if (device->drawable) {
        CFRelease((__bridge CFTypeRef)device->drawable);
        device->drawable = nil;
    }

    device->encoder = nil;
    device->commandBuffer = nil;
    device->targetDepth = -1;

    // Advance to the next ring slot for the next frame.
    device->frameIndex++;
}

// Builds a render-pass descriptor for a colour target with the given load action + clear colour, and
// opens an encoder on a fresh command buffer. Returns the (retained) command buffer and encoder via
// out params. Used by both begin_offscreen and end_offscreen's reopen-parent path.
static void openTarget(SakuraMetalDevice* device, id<MTLTexture> texture, MTLLoadAction loadAction,
                       float r, float g, float b, float a,
                       id<MTLCommandBuffer>* outCmd, id<MTLRenderCommandEncoder>* outEnc)
{
    id<MTLCommandBuffer> cmd = [device->queue commandBuffer];

    MTLRenderPassDescriptor* pass = [MTLRenderPassDescriptor renderPassDescriptor];
    pass.colorAttachments[0].texture = texture;
    pass.colorAttachments[0].loadAction = loadAction;
    pass.colorAttachments[0].storeAction = MTLStoreActionStore;
    pass.colorAttachments[0].clearColor = MTLClearColorMake(r, g, b, a);

    id<MTLRenderCommandEncoder> enc = [cmd renderCommandEncoderWithDescriptor:pass];

    *outCmd = cmd; CFRetain((__bridge CFTypeRef)cmd);
    *outEnc = enc; CFRetain((__bridge CFTypeRef)enc);
}

void sakura_metal_begin_offscreen(SakuraMetalDevice* device, SakuraMetalTexture* texture, float r, float g, float b, float a)
{
    if (device == NULL || texture == NULL || texture->texture == nil || device->targetDepth < 0)
        return;

    if (device->targetDepth + 1 >= SAKURA_METAL_MAX_TARGETS)
    {
        NSLog(@"sakura-metal: offscreen target stack overflow (max %d)", SAKURA_METAL_MAX_TARGETS);
        return;
    }

    // Finish recording the current (parent) target: end its encoder and commit its command buffer so
    // the serial queue executes it before the offscreen pass we're about to record. We release the
    // parent's encoder/command buffer now; when we ascend back to it, end_offscreen opens a fresh
    // command buffer for it with a LOAD action so its already-rendered contents are preserved.
    SakuraMetalTarget* parent = &device->targets[device->targetDepth];
    if (parent->encoder) { 
        [parent->encoder endEncoding]; 
        CFRelease((__bridge CFTypeRef)parent->encoder); 
        parent->encoder = nil; 
    }
    if (parent->commandBuffer) { 
        [parent->commandBuffer commit]; 
        CFRelease((__bridge CFTypeRef)parent->commandBuffer); 
        parent->commandBuffer = nil; 
    }

    id<MTLCommandBuffer> cmd = nil;
    id<MTLRenderCommandEncoder> enc = nil;
    openTarget(device, texture->texture, MTLLoadActionClear, r, g, b, a, &cmd, &enc);

    device->targetDepth++;
    SakuraMetalTarget* child = &device->targets[device->targetDepth];
    child->texture = texture->texture;
    child->commandBuffer = cmd;
    child->encoder = enc;

    device->commandBuffer = cmd;
    device->encoder = enc;
}

void sakura_metal_end_offscreen(SakuraMetalDevice* device)
{
    if (device == NULL || device->targetDepth <= 0)
        return;

    // Finish the offscreen pass: end its encoder, commit its command buffer (so its writes to the
    // render-target texture complete before the parent reload reads them), and release both.
    SakuraMetalTarget* child = &device->targets[device->targetDepth];
    if (child->encoder){ 
        [child->encoder endEncoding]; 
        CFRelease((__bridge CFTypeRef)child->encoder); 
        child->encoder = nil; 
    }
    if (child->commandBuffer) { 
        [child->commandBuffer commit]; 
        CFRelease((__bridge CFTypeRef)child->commandBuffer); 
        child->commandBuffer = nil; 
    }
    child->texture = nil;

    device->targetDepth--;

    // Reopen the parent target on a fresh command buffer with a LOAD action so its prior contents
    // (drawn before begin_offscreen) survive. The parent texture is nil for the drawable.
    SakuraMetalTarget* parent = &device->targets[device->targetDepth];
    id<MTLTexture> parentTexture = parent->texture;
    if (parentTexture == nil && device->drawable != nil)
        parentTexture = device->drawable.texture;

    id<MTLCommandBuffer> cmd = nil;
    id<MTLRenderCommandEncoder> enc = nil;
    openTarget(device, parentTexture, MTLLoadActionLoad, 0, 0, 0, 0, &cmd, &enc);

    parent->commandBuffer = cmd;
    parent->encoder       = enc;
    device->commandBuffer = cmd;
    device->encoder       = enc;
}

void sakura_metal_resize(SakuraMetalDevice* device, int width, int height, float contentsScale)
{
    if (device == NULL || device->layer == nil || width <= 0 || height <= 0)
        return;

    // contentsScale must match the backing scale so Core Animation composites the layer at native
    // (physical) resolution. Left at the default 1.0 on a Retina display, the layer is treated as
    // logical-sized and upscaled, making everything look soft.
    if (contentsScale > 0.0f)
        device->layer.contentsScale = (CGFloat)contentsScale;

    device->layer.drawableSize = CGSizeMake((CGFloat)width, (CGFloat)height);
}

// Pipeline

// Compiles one MSL source string into a library and returns the named function (autoreleased).
static id<MTLFunction> compileFunction(id<MTLDevice> device, const char* msl, const char* name)
{
    if (msl == NULL)
        return nil;

    NSError* error = nil;
    NSString* source = [NSString stringWithUTF8String:msl];

    id<MTLLibrary> library = [device newLibraryWithSource:source options:nil error:&error];
    if (library == nil)
    {
        NSLog(@"sakura-metal: MSL compile failed: %@", error);
        return nil;
    }

    id<MTLFunction> fn = [library newFunctionWithName:[NSString stringWithUTF8String:name]];
    if (fn == nil)
        NSLog(@"sakura-metal: function '%s' not found in compiled library", name);

    return fn;
}

// Configures the colour attachment's blend state for the given BlendingMode (matches the C# enum).
// Mirrors GLRenderer.SetBlendMode's glBlendFunc choices. All modes use additive blend ops on RGB and
// alpha; only the factors differ.
static void configureBlend(MTLRenderPipelineColorAttachmentDescriptor* att, int blendMode)
{
    att.blendingEnabled = YES;
    att.rgbBlendOperation = MTLBlendOperationAdd;
    att.alphaBlendOperation = MTLBlendOperationAdd;

    switch (blendMode)
    {
        case 1: // Additive: SrcAlpha, One
            att.sourceRGBBlendFactor = MTLBlendFactorSourceAlpha;
            att.sourceAlphaBlendFactor = MTLBlendFactorSourceAlpha;
            att.destinationRGBBlendFactor = MTLBlendFactorOne;
            att.destinationAlphaBlendFactor = MTLBlendFactorOne;
            break;
        case 2: // Opaque: One, Zero
            att.sourceRGBBlendFactor = MTLBlendFactorOne;
            att.sourceAlphaBlendFactor = MTLBlendFactorOne;
            att.destinationRGBBlendFactor = MTLBlendFactorZero;
            att.destinationAlphaBlendFactor = MTLBlendFactorZero;
            break;
        case 3: // Multiply: DstColor, OneMinusSrcAlpha
            att.sourceRGBBlendFactor = MTLBlendFactorDestinationColor;
            att.sourceAlphaBlendFactor  = MTLBlendFactorDestinationColor;
            att.destinationRGBBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
            att.destinationAlphaBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
            break;
        case 4: // Screen: One, OneMinusSrcColor
            att.sourceRGBBlendFactor = MTLBlendFactorOne;
            att.sourceAlphaBlendFactor = MTLBlendFactorOne;
            att.destinationRGBBlendFactor = MTLBlendFactorOneMinusSourceColor;
            att.destinationAlphaBlendFactor = MTLBlendFactorOneMinusSourceColor;
            break;
        case 5: // Premultiplied: One, OneMinusSrcAlpha
            att.sourceRGBBlendFactor = MTLBlendFactorOne;
            att.sourceAlphaBlendFactor = MTLBlendFactorOne;
            att.destinationRGBBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
            att.destinationAlphaBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
            break;
        case 0: // Alpha (default): SrcAlpha, OneMinusSrcAlpha
        default:
            att.sourceRGBBlendFactor = MTLBlendFactorSourceAlpha;
            att.sourceAlphaBlendFactor = MTLBlendFactorSourceAlpha;
            att.destinationRGBBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
            att.destinationAlphaBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
            break;
    }
}

SakuraMetalPipeline* sakura_metal_create_pipeline(
    SakuraMetalDevice* device,
    const char* vertexMsl,
    const char* fragmentMsl,
    const SakuraMetalVertexAttribute* attributes,
    int attributeCount,
    int vertexStride,
    int blendMode)
{
    if (device == NULL || device->device == nil)
        return NULL;

    // SPIRV-Cross names both entry points "main0".
    id<MTLFunction> vfn = compileFunction(device->device, vertexMsl, "main0");
    id<MTLFunction> ffn = compileFunction(device->device, fragmentMsl, "main0");
    if (vfn == nil || ffn == nil)
        return NULL;

    MTLVertexDescriptor* vd = [MTLVertexDescriptor vertexDescriptor];
    for (int i = 0; i < attributeCount; i++)
    {
        SakuraMetalVertexAttribute a = attributes[i];

        MTLVertexFormat format;
        switch (a.componentCount)
        {
            case 1: 
                format = MTLVertexFormatFloat;  
                break;
            case 2: 
                format = MTLVertexFormatFloat2; 
                break;
            case 3: 
                format = MTLVertexFormatFloat3; 
                break;
            case 4:
                format = MTLVertexFormatFloat4; 
                break;
            default: 
                format = MTLVertexFormatFloat; 
                break;
        }

        vd.attributes[a.attributeIndex].format = format;
        vd.attributes[a.attributeIndex].offset = (NSUInteger)a.offset;
        vd.attributes[a.attributeIndex].bufferIndex = SAKURA_METAL_VERTEX_BUFFER_INDEX;
    }
    vd.layouts[SAKURA_METAL_VERTEX_BUFFER_INDEX].stride       = (NSUInteger)vertexStride;
    vd.layouts[SAKURA_METAL_VERTEX_BUFFER_INDEX].stepFunction = MTLVertexStepFunctionPerVertex;

    MTLRenderPipelineDescriptor* desc = [[MTLRenderPipelineDescriptor alloc] init];
    desc.vertexFunction   = vfn;
    desc.fragmentFunction = ffn;
    desc.vertexDescriptor = vd;
    desc.colorAttachments[0].pixelFormat = device->layer.pixelFormat;

    // Blend state is baked into the pipeline in Metal, so the renderer creates one pipeline per
    // (shader, blend) pair and binds the matching variant on SetBlendMode.
    configureBlend(desc.colorAttachments[0], blendMode);

    NSError* error = nil;
    id<MTLRenderPipelineState> state = [device->device newRenderPipelineStateWithDescriptor:desc error:&error];
    if (state == nil)
    {
        NSLog(@"sakura-metal: pipeline creation failed: %@", error);
        return NULL;
    }

    SakuraMetalPipeline* p = (SakuraMetalPipeline*)calloc(1, sizeof(SakuraMetalPipeline));
    if (p == NULL)
        return NULL;

    p->state = state; CFRetain((__bridge CFTypeRef)state);
    return p;
}

void sakura_metal_destroy_pipeline(SakuraMetalPipeline* pipeline)
{
    if (pipeline == NULL)
        return;

    if (pipeline->state) CFRelease((__bridge CFTypeRef)pipeline->state);
    free(pipeline);
}

void sakura_metal_set_pipeline(SakuraMetalDevice* device, SakuraMetalPipeline* pipeline)
{
    if (device == NULL || device->encoder == nil || pipeline == NULL || pipeline->state == nil)
        return;

    [device->encoder setRenderPipelineState:pipeline->state];
}

void sakura_metal_set_vertex_uniform(SakuraMetalDevice* device, const void* data, int length, int index)
{
    if (device == NULL || device->encoder == nil || data == NULL || length <= 0)
        return;

    [device->encoder setVertexBytes:data length:(NSUInteger)length atIndex:(NSUInteger)index];
}

void sakura_metal_set_fragment_uniform(SakuraMetalDevice* device, const void* data, int length, int index)
{
    if (device == NULL || device->encoder == nil || data == NULL || length <= 0)
        return;

    [device->encoder setFragmentBytes:data length:(NSUInteger)length atIndex:(NSUInteger)index];
}

void sakura_metal_draw_triangles(SakuraMetalDevice* device, const void* data, int vertexCount, int vertexStride)
{
    if (device == NULL || device->encoder == nil || data == NULL || vertexCount <= 0)
        return;

    NSUInteger byteLength = (NSUInteger)(vertexCount * vertexStride);

    // Small draws: setVertexBytes is fast and avoids touching the shared buffer. (Limit is 4KB; stay
    // well under it.) This covers the bulk of UI quads.
    if (byteLength <= 4096)
    {
        [device->encoder setVertexBytes:data
                                 length:byteLength
                                atIndex:SAKURA_METAL_VERTEX_BUFFER_INDEX];

        [device->encoder drawPrimitives:MTLPrimitiveTypeTriangle
                            vertexStart:0
                            vertexCount:(NSUInteger)vertexCount];
        return;
    }

    // Larger draws (e.g. the expanded FpsGraph) append into this frame's ring buffer at the current
    // bump offset. The buffer is never reallocated here — it was sized in begin_frame from last frame's
    // peak, and the semaphore guarantees the GPU isn't still reading this slot. Always track the
    // requested bytes so next frame's begin_frame grows to fit.
    device->vertexBytesThisFrame += byteLength;
    if (device->vertexBytesThisFrame > device->vertexBytesLastFrame)
        device->vertexBytesLastFrame = device->vertexBytesThisFrame;

    NSUInteger drawOffset = device->vertexBufferOffset;

    // If this frame's buffer can't fit the draw (e.g. first frame, or a sudden spike), skip drawing
    // it this frame. begin_frame will have grown the buffer by next frame thanks to the peak above.
    if (device->vertexBuffer == nil || drawOffset + byteLength > device->vertexBufferCapacity)
        return;

    memcpy((char*)device->vertexBuffer.contents + drawOffset, data, byteLength);
    device->vertexBufferOffset += byteLength;

    [device->encoder setVertexBuffer:device->vertexBuffer
                              offset:drawOffset
                             atIndex:SAKURA_METAL_VERTEX_BUFFER_INDEX];

    [device->encoder drawPrimitives:MTLPrimitiveTypeTriangle
                        vertexStart:0
                        vertexCount:(NSUInteger)vertexCount];
}

// Texture

SakuraMetalTexture* sakura_metal_create_texture(SakuraMetalDevice* device, int width, int height)
{
    if (device == NULL || device->device == nil || width <= 0 || height <= 0)
        return NULL;

    // Mipmapped (matching GL's LinearMipmapLinear) so minified textures — small text in the atlas,
    // downscaled sprites — don't alias. Mip levels are filled by a blit after each upload.
    // mipmapped:YES only makes sense for textures larger than 1x1; a 1x1 (white pixel) has one level.
    BOOL wantMips = (width > 1 || height > 1);

    MTLTextureDescriptor* td = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm_sRGB
                                     width:(NSUInteger)width
                                    height:(NSUInteger)height
                                 mipmapped:wantMips];
    td.usage = MTLTextureUsageShaderRead;

    id<MTLTexture> texture = [device->device newTextureWithDescriptor:td];
    if (texture == nil)
        return NULL;

    SakuraMetalTexture* t = (SakuraMetalTexture*)calloc(1, sizeof(SakuraMetalTexture));
    if (t == NULL)
        return NULL;

    t->texture = texture; 
    CFRetain((__bridge CFTypeRef)texture);
    t->queue = device->queue; 
    CFRetain((__bridge CFTypeRef)device->queue);
    t->hasMips = wantMips;
    return t;
}

SakuraMetalTexture* sakura_metal_create_render_target(SakuraMetalDevice* device, int width, int height)
{
    if (device == NULL || device->device == nil || width <= 0 || height <= 0)
        return NULL;

    // Renderable + sampleable, single mip level, sRGB (matches the drawable + GL's Srgb8Alpha8 RTs).
    MTLTextureDescriptor* td = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm_sRGB
                                     width:(NSUInteger)width
                                    height:(NSUInteger)height
                                 mipmapped:NO];
    td.usage       = MTLTextureUsageRenderTarget | MTLTextureUsageShaderRead;
    td.storageMode = MTLStorageModePrivate; // GPU-only; never CPU-uploaded.

    id<MTLTexture> texture = [device->device newTextureWithDescriptor:td];
    if (texture == nil)
        return NULL;

    SakuraMetalTexture* t = (SakuraMetalTexture*)calloc(1, sizeof(SakuraMetalTexture));
    if (t == NULL)
        return NULL;

    t->texture = texture; CFRetain((__bridge CFTypeRef)texture);
    t->queue = nil; // no mip blits for render targets
    t->hasMips = NO;
    t->isRenderTarget = YES;
    return t;
}

// Allocates a BGRA8 copy of RGBA8 input (R<->B swizzled) so colours match the BGRA texture format.
// Caller frees. Returns NULL on alloc failure.
static uint8_t* swizzleRgbaToBgra(const void* data, int count)
{
    const uint8_t* src = (const uint8_t*)data;
    uint8_t* bgra = (uint8_t*)malloc((size_t)count * 4);
    if (bgra == NULL)
        return NULL;

    for (int i = 0; i < count; i++)
    {
        bgra[i * 4 + 0] = src[i * 4 + 2]; // B <- R
        bgra[i * 4 + 1] = src[i * 4 + 1]; // G
        bgra[i * 4 + 2] = src[i * 4 + 0]; // R <- B
        bgra[i * 4 + 3] = src[i * 4 + 3]; // A
    }

    return bgra;
}

void sakura_metal_upload_texture(SakuraMetalTexture* texture, const void* data, int width, int height)
{
    sakura_metal_upload_texture_region(texture, 0, 0, width, height, data);
}

void sakura_metal_upload_texture_region(SakuraMetalTexture* texture, int x, int y, int width, int height, const void* data)
{
    if (texture == NULL || texture->texture == nil || data == NULL || width <= 0 || height <= 0)
        return;

    // Texture is BGRA8 (matches the drawable format); framework supplies RGBA8 — swizzle.
    uint8_t* bgra = swizzleRgbaToBgra(data, width * height);
    if (bgra == NULL)
        return;

    MTLRegion region = MTLRegionMake2D((NSUInteger)x, (NSUInteger)y, (NSUInteger)width, (NSUInteger)height);
    [texture->texture replaceRegion:region
                        mipmapLevel:0
                          withBytes:bgra
                        bytesPerRow:(NSUInteger)(width * 4)];

    free(bgra);

    // Regenerate the mip chain so minified sampling stays crisp (mirrors GL's per-upload
    // glGenerateMipmap). Uploads are infrequent after warmup, so a one-shot blit here is fine.
    if (texture->hasMips && texture->queue != nil)
    {
        id<MTLCommandBuffer> cb = [texture->queue commandBuffer];
        id<MTLBlitCommandEncoder> blit = [cb blitCommandEncoder];
        [blit generateMipmapsForTexture:texture->texture];
        [blit endEncoding];
        [cb commit];
    }
}

void sakura_metal_destroy_texture(SakuraMetalTexture* texture)
{
    if (texture == NULL)
        return;

    if (texture->texture) 
        CFRelease((__bridge CFTypeRef)texture->texture);
    if (texture->queue)   
        CFRelease((__bridge CFTypeRef)texture->queue);
    free(texture);
}

SakuraMetalTexture* sakura_metal_create_plane_texture(SakuraMetalDevice* device, int width, int height)
{
    if (device == NULL || device->device == nil || width <= 0 || height <= 0)
        return NULL;

    // Single-channel R8Unorm — raw luma/chroma samples, NOT sRGB (the video shader does its own
    // YUV→RGB + gamma). No mips; sampled with the clamp sampler.
    MTLTextureDescriptor* td = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatR8Unorm
                                     width:(NSUInteger)width
                                    height:(NSUInteger)height
                                 mipmapped:NO];
    td.usage = MTLTextureUsageShaderRead;

    id<MTLTexture> texture = [device->device newTextureWithDescriptor:td];
    if (texture == nil)
        return NULL;

    SakuraMetalTexture* t = (SakuraMetalTexture*)calloc(1, sizeof(SakuraMetalTexture));
    if (t == NULL)
        return NULL;

    t->texture = texture; CFRetain((__bridge CFTypeRef)texture);
    t->queue = nil;
    t->hasMips = NO;
    t->isPlane = YES;
    return t;
}

void sakura_metal_upload_plane(SakuraMetalTexture* texture, const void* data, int width, int height, int bytesPerRow)
{
    if (texture == NULL || texture->texture == nil || data == NULL || width <= 0 || height <= 0)
        return;

    // Single channel, no swizzle. bytesPerRow is the source stride (FFmpeg linesize ≥ width). Metal's
    // replaceRegion reads `height` rows of that stride and copies the leading `width` bytes of each.
    MTLRegion region = MTLRegionMake2D(0, 0, (NSUInteger)width, (NSUInteger)height);
    [texture->texture replaceRegion:region
                        mipmapLevel:0
                          withBytes:data
                        bytesPerRow:(NSUInteger)(bytesPerRow > 0 ? bytesPerRow : width)];
}

void sakura_metal_set_fragment_texture(SakuraMetalDevice* device, SakuraMetalTexture* texture, int slot)
{
    if (device == NULL || device->encoder == nil || texture == NULL || texture->texture == nil)
        return;

    [device->encoder setFragmentTexture:texture->texture atIndex:(NSUInteger)slot];

    // Render targets and video planes sample with the clamp sampler (the blur reads beyond the edges
    // and must clamp; video planes must not wrap luma/chroma at the edges); normal textures use the
    // repeat+mip sampler (required for FillMode.Tile).
    id<MTLSamplerState> s = ((texture->isRenderTarget || texture->isPlane) && device->clampSampler)
        ? device->clampSampler : device->sampler;
    if (s)
        [device->encoder setFragmentSamplerState:s atIndex:(NSUInteger)slot];
}

void sakura_metal_set_fragment_texture_wrap(SakuraMetalDevice* device, SakuraMetalTexture* texture, int slot, int repeat)
{
    if (device == NULL || device->encoder == nil || texture == NULL || texture->texture == nil)
        return;

    [device->encoder setFragmentTexture:texture->texture atIndex:(NSUInteger)slot];

    // Caller-forced wrap (video planes: Tile → repeat, else clamp). The repeat sampler is mip-filtered,
    // which is harmless for the single-level R8 plane textures.
    id<MTLSamplerState> s = (repeat != 0) ? device->sampler : device->clampSampler;
    if (s)
        [device->encoder setFragmentSamplerState:s atIndex:(NSUInteger)slot];
}
