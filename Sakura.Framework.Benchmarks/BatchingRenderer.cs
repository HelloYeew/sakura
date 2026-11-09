// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Batches;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Benchmarks;

/// <summary>
/// A headless renderer that batches the way the GPU backends do and throws each batch away where they would upload and
/// draw it. It runs the real <see cref="VertexBatch"/>, the clip stack, the texture slots, and the flushes that blend
/// changes, borders and edge effects force. That is what a frame costs the draw thread, without the driver.
/// </summary>
/// <remarks>
/// The batch comes in both shapes: non-indexed as on Metal (6 vertices a quad), and indexed as on Direct3D 11, Vulkan
/// and OpenGL (4 vertices and 6 indices a quad). Both hold 12,000 vertices and 16 texture slots, as every backend does.
/// Only the members the draw nodes reach are batched here; textures, shaders and framebuffers stay the headless ones.
/// </remarks>
internal sealed class BatchingRenderer : HeadlessRenderer, IRenderer
{
    private const int max_batch_vertices = 1000 * 12;
    private const int max_texture_slots = 16;

    private readonly VertexBatch batch;
    private readonly ClipStack clips = new ClipStack();
    private readonly Stack<ClipState> frameBufferClips = new Stack<ClipState>();

    // What each texture slot holds, by backend texture: the CPU mirror the backends keep of what's bound.
    private readonly INativeTexture?[] boundTextures = new INativeTexture?[max_texture_slots];
    private int boundTextureCount;

    private BlendingMode currentBlendMode = BlendingMode.Alpha;
    private DrawNode? root;

    /// <summary>
    /// Whether the batch is indexed, as on Direct3D 11, Vulkan and OpenGL, rather than non-indexed, as on Metal.
    /// </summary>
    public bool Indexed => batch.Indexed;

    /// <summary>
    /// The batches the last frame flushed: the draw calls a backend would have made.
    /// </summary>
    public int DrawCalls { get; private set; }

    /// <summary>
    /// The vertices the last frame's batches held.
    /// </summary>
    public int VerticesDrawn { get; private set; }

    /// <summary>
    /// Creates the renderer with a batch of the given shape.
    /// </summary>
    /// <param name="textureManager">Where the white pixel and every texture come from.</param>
    /// <param name="indexed">Indexed as on Direct3D 11, Vulkan and OpenGL, or not, as on Metal.</param>
    public BatchingRenderer(HeadlessTextureManager textureManager, bool indexed)
        : base(textureManager)
    {
        batch = new VertexBatch(max_batch_vertices, FlushBatch, indexed);
    }

    /// <summary>
    /// Keeps the root the next <see cref="Draw"/> walks.
    /// </summary>
    public new void SetRoot(DrawNode rootDrawNode) => root = rootDrawNode;

    /// <summary>
    /// Starts a frame as a backend does: alpha blending, no mask, and the white pixel in every texture slot.
    /// </summary>
    public new void StartFrame()
    {
        base.StartFrame();

        currentBlendMode = BlendingMode.Alpha;
        clips.Reset();
        frameBufferClips.Clear();
        resetTextureSlots();
        DrawCalls = 0;
        VerticesDrawn = 0;
    }

    /// <summary>
    /// Walks the root's draw nodes, then flushes what's left, as a backend ends its frame.
    /// </summary>
    public new void Draw(IClock clock)
    {
        root?.Draw(this);
        FlushBatch();
    }

    /// <summary>
    /// Adds a triangle list to the batch, clipped by the current mask.
    /// </summary>
    public new void DrawVertices(ReadOnlySpan<SakuraVertex> vertices, Texture textureGl)
    {
        if (vertices.Length == 0)
            return;

        float textureIndex = prepareTexture(textureGl);
        batch.AddRange(vertices, textureIndex, clips.Current);
    }

    /// <summary>
    /// Adds quads to the batch, clipped by the current mask.
    /// </summary>
    public new void DrawQuads(ReadOnlySpan<SakuraVertex> vertices, Texture textureGl)
    {
        float textureIndex = prepareTexture(textureGl);
        var clip = clips.Current;

        for (int i = 0; i + 4 <= vertices.Length; i += 4)
        {
            batch.AddQuad(vertices.Slice(i, 4), textureIndex, clip);
        }
    }

    /// <summary>
    /// Makes the mask, inside the current one, the clip stamped onto every vertex drawn next.
    /// </summary>
    public new void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius) => clips.Push(maskCenter, maskHalfSize, shearX, cornerRadius);

    /// <summary>
    /// Makes the mask, inside the current one and fading in from its edges, the clip stamped onto every vertex drawn next.
    /// </summary>
    public void PushMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, MarginPadding fade) => clips.Push(maskCenter, maskHalfSize, shearX, cornerRadius, fade);

    /// <summary>
    /// Stamps the current mask onto <paramref name="vertices"/>.
    /// </summary>
    public new void ApplyCurrentClip(Span<SakuraVertex> vertices) => clips.ApplyTo(vertices);

    /// <summary>
    /// Goes back to the mask before the matching push, and draws the container's border, which a backend draws in a
    /// batch of its own: flushed before, for the border's uniforms, and after.
    /// </summary>
    public new void PopMask(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float borderThickness, Color borderColor, ReadOnlySpan<SakuraVertex> maskVertices = default)
    {
        clips.Pop();

        if (borderThickness <= 0 || maskVertices.Length < 4)
            return;

        FlushBatch();
        DrawQuads(maskVertices[..4], WhitePixel);
        FlushBatch();
    }

    /// <summary>
    /// Draws a glow or a shadow in a batch of its own, as a backend does for the effect's uniforms, in additive blending
    /// for a glow.
    /// </summary>
    public new void DrawEdgeEffect(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, float edgeRadius, Vector2 offset, Color color, bool glow, bool hollow, ReadOnlySpan<SakuraVertex> quadVertices)
    {
        if (color.A == 0 || quadVertices.Length < 4)
            return;

        FlushBatch();

        var previousBlend = currentBlendMode;

        if (glow)
            SetBlendMode(BlendingMode.Additive);

        DrawQuads(quadVertices[..4], WhitePixel);
        FlushBatch();

        if (glow)
            SetBlendMode(previousBlend);
    }

    /// <summary>
    /// Changes the blending for what's drawn next, flushing what's batched under the old one first.
    /// </summary>
    public new void SetBlendMode(BlendingMode blendingMode)
    {
        if (blendingMode == currentBlendMode)
            return;

        FlushBatch();
        currentBlendMode = blendingMode;
    }

    /// <summary>
    /// Starts drawing into a framebuffer: what's batched goes out first, and the content starts unclipped.
    /// </summary>
    public new void BindFrameBuffer(IFrameBuffer frameBuffer, RectangleF sourceRect, Color clearColor = default)
    {
        FlushBatch();
        frameBufferClips.Push(clips.Current);
        clips.Current = ClipState.None;
        base.BindFrameBuffer(frameBuffer, sourceRect, clearColor);
    }

    /// <summary>
    /// Goes back to the target drawn into before, with its clip.
    /// </summary>
    public new void UnbindFrameBuffer()
    {
        FlushBatch();

        if (frameBufferClips.Count > 0)
            clips.Current = frameBufferClips.Pop();

        base.UnbindFrameBuffer();
    }

    /// <summary>
    /// Counts what's batched as one draw call and throws it away, where a backend would upload and draw it.
    /// </summary>
    public new void FlushBatch()
    {
        if (batch.IsEmpty)
            return;

        DrawCalls++;
        VerticesDrawn += batch.VertexCount;
        batch.Reset();
    }

    /// <summary>
    /// The texture slot <paramref name="texture"/> is in, binding it to a free one if it's in none, and flushing to start
    /// a fresh set when all are taken. Keyed by the backend texture, as the backends key by their native handle.
    /// </summary>
    private float prepareTexture(Texture? texture)
    {
        var native = texture?.BackendTexture ?? WhitePixel.BackendTexture;

        for (int i = 0; i < boundTextureCount; i++)
        {
            if (ReferenceEquals(boundTextures[i], native))
                return i;
        }

        if (boundTextureCount < max_texture_slots)
        {
            boundTextures[boundTextureCount] = native;
            return boundTextureCount++;
        }

        FlushBatch();
        Array.Clear(boundTextures);
        boundTextures[0] = native;
        boundTextureCount = 1;
        return 0;
    }

    /// <summary>
    /// Puts the white pixel in slot 0 and empties the rest, as a backend does when a frame or a pass starts.
    /// </summary>
    private void resetTextureSlots()
    {
        Array.Clear(boundTextures);
        boundTextures[0] = WhitePixel.BackendTexture;
        boundTextureCount = 1;
    }
}
