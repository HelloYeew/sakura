// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Draw node for <see cref="BufferedContainer"/>: renders the child draw nodes into the
/// container's shared offscreen framebuffer, runs the active effect passes (separable
/// Gaussian blur, grayscale), then composites the result — and optionally the original —
/// to the current render target.
/// </summary>
public class BufferedContainerDrawNode : ContainerDrawNode
{
    // Effect shaders are process-global: one program each serves every buffered container.
    // Created lazily on the draw thread.
    private static IShader? blur_shader;
    private static IShader? grayscale_shader;
    private static IRenderer? shader_renderer;

    private const int max_blur_radius = 64;

    private BufferedContainer.BufferedContainerSharedData? shared;

    private bool cacheDrawnFrameBuffer;
    private bool pixelSnapping;
    private Vector2 frameBufferScale = Vector2.One;
    private Vector2 blurSigma;
    private float blurRotation;
    private float grayscaleStrength;
    private bool drawOriginal;
    private Vector4 effectColourLinear = new Vector4(1, 1, 1, 1);
    private BlendingMode? effectBlending;
    private EffectPlacement effectPlacement;
    private Color backgroundColour;

    public override void ApplyState(Drawable source)
    {
        base.ApplyState(source);

        var buffered = (BufferedContainer)source;
        shared = buffered.SharedData;
        cacheDrawnFrameBuffer = buffered.CacheDrawnFrameBuffer;
        pixelSnapping = buffered.PixelSnapping;
        frameBufferScale = buffered.FrameBufferScale;
        blurSigma = buffered.BlurSigma;
        blurRotation = buffered.BlurRotation;
        grayscaleStrength = buffered.GrayscaleStrength;
        drawOriginal = buffered.DrawOriginal;
        effectBlending = buffered.EffectBlending;
        effectPlacement = buffered.EffectPlacement;
        backgroundColour = buffered.BackgroundColour;

        var ec = buffered.EffectColour;
        effectColourLinear = new Vector4(
            ColorExtensions.SrgbToLinear(ec.R),
            ColorExtensions.SrgbToLinear(ec.G),
            ColorExtensions.SrgbToLinear(ec.B),
            ec.A / 255f);
    }

    private bool blurActive => blurSigma.X > 0 || blurSigma.Y > 0;
    private bool grayscaleActive => grayscaleStrength > 0;

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || shared == null)
            return;

        var rect = DrawRectangle;

        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // Buffer size in physical pixels (DPI-aware), scaled by FrameBufferScale.
        var renderScale = renderer.RenderScale;
        int targetWidth = Math.Max(1, (int)MathF.Ceiling(rect.Width * renderScale.X * frameBufferScale.X));
        int targetHeight = Math.Max(1, (int)MathF.Ceiling(rect.Height * renderScale.Y * frameBufferScale.Y));

        // Effect passes require GL-specific operations (raw vertex upload + manual binds).
        bool effectsActive = (blurActive || grayscaleActive) && renderer is IGLRenderer;

        bool needsRedraw = !cacheDrawnFrameBuffer || shared.RenderedVersion != AppliedSubtreeVersion;

        if (shared.FrameBuffer == null)
        {
            shared.FrameBuffer = renderer.CreateFrameBuffer(targetWidth, targetHeight, pixelSnapping);
            needsRedraw = true;
        }
        else if (shared.FrameBuffer.Width != targetWidth || shared.FrameBuffer.Height != targetHeight)
        {
            // The resize deletes and recreates the attachment texture. Any geometry still
            // batched (possibly referencing a texture whose handle could alias the deleted
            // one) must be drawn before the deletion happens.
            renderer.FlushBatch();

            shared.FrameBuffer.Resize(targetWidth, targetHeight);
            needsRedraw = true;
        }

        if (needsRedraw)
        {
            renderer.BindFrameBuffer(shared.FrameBuffer, rect, backgroundColour);

            // Children render with their normal screen-space coordinates (the bound
            // projection maps the captured rect onto the buffer), including any masking
            // this container itself has enabled.
            base.Draw(renderer);

            renderer.UnbindFrameBuffer();

            shared.FinalEffectBuffer = effectsActive
                ? runEffectPasses((IGLRenderer)renderer, rect, targetWidth, targetHeight, renderScale)
                : null;

            shared.RenderedVersion = AppliedSubtreeVersion;
        }

        drawComposite(renderer, rect);
    }

    /// <summary>
    /// Runs the active effect passes, ping-ponging between the effect buffers.
    /// The original content in <see cref="BufferedContainer.BufferedContainerSharedData.FrameBuffer"/>
    /// is left untouched (needed when <see cref="BufferedContainer.DrawOriginal"/> is set).
    /// </summary>
    /// <returns>The buffer holding the final effect result.</returns>
    private IFrameBuffer runEffectPasses(IGLRenderer renderer, RectangleF rect, int targetWidth, int targetHeight, Vector2 renderScale)
    {
        if (blur_shader == null || !ReferenceEquals(shader_renderer, renderer))
        {
            blur_shader = renderer.CreateShader(renderer.ShaderStorage, "shader.vert", "blur.frag");
            grayscale_shader = renderer.CreateShader(renderer.ShaderStorage, "shader.vert", "grayscale.frag");
            shader_renderer = renderer;
        }

        for (int i = 0; i < shared!.EffectBuffers.Length; i++)
        {
            if (shared.EffectBuffers[i] == null)
                shared.EffectBuffers[i] = renderer.CreateFrameBuffer(targetWidth, targetHeight, pixelSnapping);
            else
                shared.EffectBuffers[i]!.Resize(targetWidth, targetHeight);
        }

        IFrameBuffer current = shared.FrameBuffer!;
        int pingPong = 0;

        IFrameBuffer nextTarget()
        {
            var target = shared.EffectBuffers[pingPong]!;
            pingPong ^= 1;
            return target;
        }

        // The passes must write exact values (no alpha blending against the cleared buffer).
        renderer.SetBlendMode(BlendingMode.Opaque);

        if (blurActive)
        {
            // Sigma is specified in logical pixels; the shader samples in buffer texels.
            float sigmaX = blurSigma.X * renderScale.X * frameBufferScale.X;
            float sigmaY = blurSigma.Y * renderScale.Y * frameBufferScale.Y;

            // BlurRotation rotates the two orthogonal pass directions.
            float radians = blurRotation * MathF.PI / 180f;
            var directionX = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
            var directionY = new Vector2(-MathF.Sin(radians), MathF.Cos(radians));

            var target = nextTarget();
            blurPass(renderer, current, target, rect, directionX, sigmaX);
            current = target;

            target = nextTarget();
            blurPass(renderer, current, target, rect, directionY, sigmaY);
            current = target;
        }

        if (grayscaleActive)
        {
            var target = nextTarget();
            grayscalePass(renderer, current, target, rect);
            current = target;
        }

        renderer.SetBlendMode(BlendingMode.Alpha);

        return current;
    }

    private void blurPass(IGLRenderer renderer, IFrameBuffer source, IFrameBuffer target, RectangleF rect, Vector2 direction, float sigmaTexels)
    {
        int radius = sigmaTexels > 0 ? Math.Min(max_blur_radius, (int)MathF.Ceiling(sigmaTexels * 3)) : 0;

        runShaderPass(renderer, source, target, rect, blur_shader!, shader =>
        {
            shader.SetUniformBlock("BlurBlock", new Uniforms.BlurBlock
            {
                TexelSize = new Vector2(1f / source.Width, 1f / source.Height),
                Direction = new Vector2(direction.X, direction.Y),
                Sigma = sigmaTexels,
                Radius = radius,
            });
        });
    }

    private void grayscalePass(IGLRenderer renderer, IFrameBuffer source, IFrameBuffer target, RectangleF rect)
    {
        runShaderPass(renderer, source, target, rect, grayscale_shader!, shader =>
            shader.SetUniformBlock("GrayscaleBlock", new Uniforms.GrayscaleBlock
            {
                Strength = grayscaleStrength
            }));
    }

    /// <summary>
    /// Draws a full-buffer quad from <paramref name="source"/> into <paramref name="target"/>
    /// using a custom shader. Follows the framework's custom-shader pattern:
    /// flush → bind shader + uniforms → raw draw → restore main shader.
    /// </summary>
    private static void runShaderPass(IGLRenderer renderer, IFrameBuffer source, IFrameBuffer target, RectangleF rect, IShader shader, Action<IShader> setUniforms)
    {
        renderer.BindFrameBuffer(target, rect);
        renderer.FlushBatch();

        shader.Use();
        shader.SetUniformBlock("ProjectionBlock", new Uniforms.ProjectionBlock
        {
            Projection = renderer.ProjectionMatrix
        });
        shader.SetUniform("u_Texture", 0);
        setUniforms(shader);

        // Bind the source attachment to unit 0 for the pass.
        if (source.Texture.BackendTexture is GLTexture glTexture)
            glTexture.Bind(0);

        Span<Vertex.Vertex> quad = stackalloc Vertex.Vertex[4];
        fillQuad(quad, rect, new Vector4(1, 1, 1, 1));

        renderer.DrawVerticesRaw(quad);
        renderer.RestoreMainShader();

        renderer.UnbindFrameBuffer();
    }

    private void drawComposite(IRenderer renderer, RectangleF rect)
    {
        // The container's own vertex color is Color(linear) with alpha = DrawAlpha * Color.A.
        // Children inside the buffer already carry the cascaded DrawAlpha (alpha propagates
        // to children in this framework; color does not), so the composite quads must apply
        // only the color tint and the color's own alpha — strip DrawAlpha to avoid
        // double-applying the fade.
        Vector4 baseColour = Vertices.Length > 0 ? Vertices[0].Color : new Vector4(1, 1, 1, DrawAlpha);
        baseColour.W = DrawAlpha > 0 ? baseColour.W / DrawAlpha : 0;

        var effectBuffer = shared!.FinalEffectBuffer;

        if (effectBuffer == null)
        {
            // No effects: just the original content.
            drawQuad(renderer, shared.FrameBuffer!, rect, baseColour, Blending);
            return;
        }

        var effectQuadColour = new Vector4(
            baseColour.X * effectColourLinear.X,
            baseColour.Y * effectColourLinear.Y,
            baseColour.Z * effectColourLinear.Z,
            baseColour.W * effectColourLinear.W);

        var effectQuadBlending = effectBlending ?? Blending;

        if (effectPlacement == EffectPlacement.Behind)
        {
            drawQuad(renderer, effectBuffer, rect, effectQuadColour, effectQuadBlending);

            if (drawOriginal)
                drawQuad(renderer, shared.FrameBuffer!, rect, baseColour, Blending);
        }
        else
        {
            if (drawOriginal)
                drawQuad(renderer, shared.FrameBuffer!, rect, baseColour, Blending);

            drawQuad(renderer, effectBuffer, rect, effectQuadColour, effectQuadBlending);
        }
    }

    private static void drawQuad(IRenderer renderer, IFrameBuffer buffer, RectangleF rect, Vector4 colour, BlendingMode blending)
    {
        Span<Vertex.Vertex> quad = stackalloc Vertex.Vertex[4];
        fillQuad(quad, rect, colour);

        renderer.SetBlendMode(blending);
        renderer.DrawQuads(quad, buffer.Texture);
    }

    /// <summary>
    /// Fills a screen-space axis-aligned quad covering <paramref name="rect"/> with
    /// V-flipped texture coordinates (GL stores framebuffer row 0 at the projection's
    /// bottom edge). Used for both the effect passes and the final composite, which keeps
    /// the content orientation consistent through every pass.
    /// </summary>
    private static void fillQuad(Span<Vertex.Vertex> quad, RectangleF rect, Vector4 color)
    {
        quad[0] = new Vertex.Vertex { Position = new Vector2(rect.X, rect.Y), Color = color, TexCoords = new Vector2(0, 1), ClipData = new Vector4(0, 0, -1, -1) };
        quad[1] = new Vertex.Vertex { Position = new Vector2(rect.X + rect.Width, rect.Y), Color = color, TexCoords = new Vector2(1, 1), ClipData = new Vector4(0, 0, -1, -1) };
        quad[2] = new Vertex.Vertex { Position = new Vector2(rect.X + rect.Width, rect.Y + rect.Height), Color = color, TexCoords = new Vector2(1, 0), ClipData = new Vector4(0, 0, -1, -1) };
        quad[3] = new Vertex.Vertex { Position = new Vector2(rect.X, rect.Y + rect.Height), Color = color, TexCoords = new Vector2(0, 0), ClipData = new Vector4(0, 0, -1, -1) };
    }
}
