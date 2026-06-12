// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Rendering;

public interface IRenderer
{
    /// <summary>
    /// A 1x1 white pixel texture managed by the renderer.
    /// </summary>
    Texture WhitePixel { get; }

    /// <summary>
    /// Initializes the renderer to be used with the specified window.
    /// </summary>
    protected internal void Initialize(IGraphicsSurface graphicsSurface);

    void Clear();

    void StartFrame();

    void SetRoot(DrawNode rootNode);

    void Resize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight);

    void Draw(IClock clock);

    void DrawVertices(ReadOnlySpan<Vertex.Vertex> vertices, Texture textureGl);

    /// <summary>
    /// Draws one or more indexed quads. <paramref name="vertices"/> must contain a multiple of
    /// 4 vertices, each quad ordered top-left, top-right, bottom-right, bottom-left.
    /// </summary>
    void DrawQuads(ReadOnlySpan<Vertex.Vertex> vertices, Texture textureGl);

    void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius);

    void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<Vertex.Vertex> maskVertices = default);

    /// <summary>
    /// Sets the blend mode for subsequent draw calls. This will affect how colors are blended when drawing.
    /// </summary>
    /// <param name="blendingMode">The blend mode to set.</param>
    void SetBlendMode(BlendingMode blendingMode);

    /// <summary>
    /// Schedules an action to be executed on the Draw thread at the start of the next frame.
    /// This is vital for OpenGL resource creation from other threads.
    /// </summary>
    void ScheduleToDrawThread(Action action);

    /// <summary>
    /// The current orthographic projection matrix used for rendering.
    /// Custom shaders must set this on their own program before drawing.
    /// </summary>
    Maths.Matrix4x4 ProjectionMatrix { get; }

    /// <summary>
    /// Flushes any pending batched geometry to the GPU immediately.
    /// Must be called before switching shader programs mid-frame.
    /// </summary>
    void FlushBatch();

    /// <summary>
    /// Restores the main scene shader and its standard uniforms after a custom shader was used.
    /// Must be called after any DrawNode that switches to a non-standard shader.
    /// </summary>
    void RestoreMainShader();

    /// <summary>
    /// Storage pointing to the framework's built-in shader directory.
    /// Use this to load the framework's own shaders, or as a base for composite stores
    /// that overlay game shaders on top of framework utilities.
    /// Set by <see cref="Platform.AppHost"/> after <see cref="Initialize"/> completes.
    /// </summary>
    Storage ShaderStorage { get; set; }

    /// <summary>
    /// Creates a backend-specific shader by loading source from a <see cref="Platform.Storage"/>.
    /// Works with any Storage: embedded resources, on-disk files, composite stores, etc.
    /// <c>#include "filename"</c> directives are resolved relative to the same Storage.
    /// Must be called on the draw thread.
    /// </summary>
    /// <param name="storage">Storage containing the shader source files.</param>
    /// <param name="vertexPath">Path inside the storage to the vertex shader (e.g. "shader.vert").</param>
    /// <param name="fragmentPath">Path inside the storage to the fragment shader (e.g. "shader.frag").</param>
    IShader CreateShader(Storage storage, string vertexPath, string fragmentPath);

    /// <summary>
    /// Creates a backend-specific YUV420P video texture of the given dimensions.
    /// Must be called on the render thread.
    /// </summary>
    INativeVideoTexture CreateVideoTexture(int width, int height);

    /// <summary>
    /// The physical-pixels-per-logical-unit scale of the current output
    /// (e.g. 2.0 on HiDPI displays). Use to size offscreen buffers in physical pixels.
    /// </summary>
    Vector2 RenderScale { get; }

    /// <summary>
    /// Creates an offscreen render target of the given size in physical pixels.
    /// Must be called on the draw thread.
    /// </summary>
    /// <param name="width">Width in physical pixels.</param>
    /// <param name="height">Height in physical pixels.</param>
    /// <param name="pixelSnapping">
    /// When true the buffer is sampled with nearest-neighbour filtering when blitted,
    /// snapping to whole pixels instead of bilinear smoothing.
    /// </param>
    IFrameBuffer CreateFrameBuffer(int width, int height, bool pixelSnapping = false);

    /// <summary>
    /// Redirects subsequent draw commands into <paramref name="frameBuffer"/>.
    /// <paramref name="sourceRect"/> is the logical screen-space rectangle the buffer
    /// captures: geometry keeps its normal screen-space coordinates and is mapped onto the
    /// buffer by an adjusted projection. The buffer is cleared to <paramref name="clearColour"/>
    /// (default: transparent black).
    /// Nesting is supported; every call must be matched by <see cref="UnbindFrameBuffer"/>.
    /// </summary>
    void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColour = default);

    /// <summary>
    /// Ends rendering into the most recently bound framebuffer, restoring the previous
    /// render target, viewport, projection and clip state.
    /// </summary>
    void UnbindFrameBuffer();
}
