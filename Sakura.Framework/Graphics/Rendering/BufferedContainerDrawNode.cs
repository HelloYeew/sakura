// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Draw node for <see cref="BufferedContainer"/>
/// </summary>
public class BufferedContainerDrawNode : ContainerDrawNode
{
    private static IShader? blurShader;
    private static IShader? grayscaleShader;
    private static IRenderer? shaderRenderer;

    private const int max_blur_radius = 64;

    /// <summary>
    /// How many consecutive passthrough frames must pass before the (now unused) framebuffers are
    /// released.
    /// </summary>
    private const int passthrough_frames_before_release = 60;

    private BufferedContainer.BufferedContainerSharedData? shared;

    private bool cacheDrawnFrameBuffer;
    private bool pixelSnapping;
    private Vector2 frameBufferScale = Vector2.One;
    private Vector2 blurSigma;
    private float blurRotation;
    private float grayscaleStrength;
    private bool drawOriginal;
    private Vector4 effectColorLinear = new Vector4(1, 1, 1, 1);
    private BlendingMode? effectBlending;
    private EffectPlacement effectPlacement;
    private Color backgroundColor;

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
        backgroundColor = buffered.BackgroundColor;

        var ec = buffered.EffectColor;
        effectColorLinear = new Vector4(
            ColorExtensions.SrgbToLinear(ec.R),
            ColorExtensions.SrgbToLinear(ec.G),
            ColorExtensions.SrgbToLinear(ec.B),
            ec.A / 255f
        );
    }

    private bool blurActive => blurSigma.X > 0 || blurSigma.Y > 0;
    private bool grayscaleActive => grayscaleStrength > 0;

    /// <summary>
    /// Whether this frame can skip the offscreen pass entirely and draw the subtree straight to the
    /// target, which is what an unconditionally-wrapping container costs nothing for.
    /// </summary>
    private bool canSkipBuffer =>
        !blurActive
        && !grayscaleActive
        // The point of caching is to keep the drawn buffer across frames; a passthrough redraws the
        // subtree every frame, which is the opposite of what was asked for.
        && !cacheDrawnFrameBuffer
        // The buffer clear has no passthrough equivalent.
        && backgroundColor.A == 0
        // A deliberate resolution change.
        && frameBufferScale.Equals(Vector2.One)
        // Applies to the flattened result, not to each child.
        && Blending == BlendingMode.Alpha
        && compositeColorIsNeutral;

    /// <summary>
    /// Whether the composite quad would be drawn at exactly neutral colour, i.e. whether it would change
    /// nothing. All four corners are checked; see <see cref="canSkipBuffer"/>.
    /// </summary>
    private bool compositeColorIsNeutral
    {
        get
        {
            // No vertices means drawComposite falls back to a colour built from DrawAlpha, which this
            // cannot verify. Buffer instead.
            if (Vertices.Length == 0)
                return false;

            foreach (var vertex in Vertices)
            {
                var color = vertex.Color;

                if (color.X != 1f || color.Y != 1f || color.Z != 1f || color.W != 1f)
                    return false;
            }

            return true;
        }
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || shared == null)
            return;

        var rect = DrawRectangle;

        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (canSkipBuffer)
        {
            // What this replaces is a single composite quad at neutral colour under the default blend, which
            // BufferedContainerCompositeTest.AnUnfadedBufferedContainerMatchesAPlainContainer pins as
            // pixel-identical to drawing the subtree straight to the target. So this is that, minus a
            // full-screen render target and a full-screen resolve.
            base.Draw(renderer);

            if (shared.FrameBuffer != null && ++shared.ConsecutivePassthroughFrames >= passthrough_frames_before_release)
                shared.Release(renderer);

            return;
        }

        shared.ConsecutivePassthroughFrames = 0;

        // Buffer size in physical pixels (DPI-aware), scaled by FrameBufferScale.
        var renderScale = renderer.RenderScale;
        int targetWidth = Math.Max(1, (int)MathF.Ceiling(rect.Width * renderScale.X * frameBufferScale.X));
        int targetHeight = Math.Max(1, (int)MathF.Ceiling(rect.Height * renderScale.Y * frameBufferScale.Y));

        // Effect passes require a raw vertex upload + manual binds, available on the GL, Metal and
        // Direct3D11 backends. Headless has no effect support.
        bool effectsActive = (blurActive || grayscaleActive) && renderer is IGLRenderer or Metal.IMetalRenderer or Direct3D11.ID3D11Renderer;

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
            renderer.BindFrameBuffer(shared.FrameBuffer, rect, backgroundColor);

            // Children render with their normal screen-space coordinates (the bound
            // projection maps the captured rect onto the buffer), including any masking
            // this container itself has enabled.
            base.Draw(renderer);

            renderer.UnbindFrameBuffer();

            shared.FinalEffectBuffer = effectsActive
                ? runEffectPasses(renderer, rect, targetWidth, targetHeight, renderScale)
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
    private IFrameBuffer runEffectPasses(IRenderer renderer, RectangleF rect, int targetWidth, int targetHeight, Vector2 renderScale)
    {
        if (blurShader == null || !ReferenceEquals(shaderRenderer, renderer))
        {
            blurShader = renderer.CreateShader(renderer.ShaderStorage, "shader.vert", "blur.frag");
            grayscaleShader = renderer.CreateShader(renderer.ShaderStorage, "shader.vert", "grayscale.frag");
            shaderRenderer = renderer;
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

        // The passes must write exact values (no alpha blending against the cleared buffer).
        renderer.SetBlendMode(BlendingMode.Opaque);

        if (blurActive)
        {
            // Sigma is specified in logical pixels, the shader samples in buffer texels.
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

        IFrameBuffer nextTarget()
        {
            var target = shared.EffectBuffers[pingPong]!;
            pingPong ^= 1;
            return target;
        }
    }

    private void blurPass(IRenderer renderer, IFrameBuffer source, IFrameBuffer target, RectangleF rect, Vector2 direction, float sigmaTexels)
    {
        int radius = sigmaTexels > 0 ? Math.Min(max_blur_radius, (int)MathF.Ceiling(sigmaTexels * 3)) : 0;

        runShaderPass(renderer, source, target, rect, blurShader!, shader =>
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

    private void grayscalePass(IRenderer renderer, IFrameBuffer source, IFrameBuffer target, RectangleF rect)
    {
        runShaderPass(renderer, source, target, rect, grayscaleShader!, shader =>
            shader.SetUniformBlock("GrayscaleBlock", new Uniforms.GrayscaleBlock
            {
                Strength = grayscaleStrength
            }));
    }

    /// <summary>
    /// Draws a full-buffer quad from <paramref name="source"/> into <paramref name="target"/>
    /// using a custom shader
    /// </summary>
    private static void runShaderPass(IRenderer renderer, IFrameBuffer source, IFrameBuffer target, RectangleF rect, IShader shader, Action<IShader> setUniforms)
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

        // Bind the source attachment to unit 0 for the pass. The backend texture is bound directly so
        // it bypasses the renderer's slot tracking (this is a raw, custom-shader pass).
        source.Texture.BackendTexture?.Bind(0);

        Span<Vertex.Vertex> quad = stackalloc Vertex.Vertex[4];
        fillQuad(quad, rect, new Vector4(1, 1, 1, 1));

        drawRaw(renderer, quad);
        renderer.RestoreMainShader();

        renderer.UnbindFrameBuffer();
    }

    /// <summary>
    /// Issues a raw vertex draw on whichever backend is active (GL or Metal). Both expose a
    /// slot-management-free raw draw through their backend-specific renderer interface.
    /// </summary>
    private static void drawRaw(IRenderer renderer, ReadOnlySpan<Vertex.Vertex> vertices)
    {
        switch (renderer)
        {
            case IGLRenderer gl:
                gl.DrawVerticesRaw(vertices);
                break;

            case Metal.IMetalRenderer metal:
                metal.DrawVerticesRaw(vertices);
                break;

            case Direct3D11.ID3D11Renderer d3D11:
                d3D11.DrawVerticesRaw(vertices);
                break;
        }
    }

    private void drawComposite(IRenderer renderer, RectangleF rect)
    {
        // The container's own vertex color is Color(linear) with alpha = DrawAlpha * Color.A, and that is
        // exactly what the composite wants. BufferedContainer is an alpha barrier
        // (BufferedContainer.ChildDrawAlpha), so children rendered into the buffer at full opacity and the
        // fade belongs here, applied once to the flattened result which is what makes fading a buffered
        // container fade the composite rather than each child.
        Vector4 baseColor = Vertices.Length > 0 ? Vertices[0].Color : new Vector4(1, 1, 1, DrawAlpha);

        var effectBuffer = shared!.FinalEffectBuffer;

        if (effectBuffer == null)
        {
            // No effects: just the original content.
            drawComposedQuad(renderer, shared.FrameBuffer!, rect, baseColor, Blending);
            return;
        }

        var effectQuadColor = new Vector4(
            baseColor.X * effectColorLinear.X,
            baseColor.Y * effectColorLinear.Y,
            baseColor.Z * effectColorLinear.Z,
            baseColor.W * effectColorLinear.W);

        var effectQuadBlending = effectBlending ?? Blending;

        if (effectPlacement == EffectPlacement.Behind)
        {
            drawComposedQuad(renderer, effectBuffer, rect, effectQuadColor, effectQuadBlending);

            if (drawOriginal)
                drawComposedQuad(renderer, shared.FrameBuffer!, rect, baseColor, Blending);
        }
        else
        {
            if (drawOriginal)
                drawComposedQuad(renderer, shared.FrameBuffer!, rect, baseColor, Blending);

            drawComposedQuad(renderer, effectBuffer, rect, effectQuadColor, effectQuadBlending);
        }
    }

    /// <summary>
    /// Draws one framebuffer back to the current target, translating both the blend mode and the quad
    /// color into the premultiplied convention a framebuffer's contents require.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why the mode. - A framebuffer's contents are <em>premultiplied</em>: children blended
    /// into it under <see cref="BlendingMode.Alpha"/> — <c>(SrcAlpha, OneMinusSrcAlpha)</c> on RGB, with
    /// alpha accumulating separately — leave RGB already carrying a factor of the accumulated alpha.
    /// Compositing that back with <see cref="BlendingMode.Alpha"/> applies the factor a second time, which
    /// darkens every partially transparent region: fringes on anti-aliased content edges, and a uniformly
    /// too-dark result for a faded container. <see cref="BlendingMode.Premultiplied"/> exists for exactly
    /// this, and makes the composite pixel-identical to drawing the subtree straight to the target.
    /// </para>
    /// <para>
    /// Why the color has to change too. - The fragment shader computes
    /// <c>texColor *= v_Color</c> componentwise, so a quad alpha below 1 scales the sampled alpha but
    /// <em>not</em> the sampled RGB. Under a straight-alpha blend that is right, because the blend applies
    /// the source alpha to RGB itself. Under a premultiplied blend nothing else ever will, so the quad
    /// color must arrive with its RGB already multiplied by its own alpha — otherwise a container faded to
    /// 0.5 composites at full brightness over a half-transparent coverage, which is a fade that lightens.
    /// </para>
    /// <para>
    /// Only the default is translated : The other modes have no premultiplied counterpart in
    /// <see cref="BlendingMode"/> — <see cref="BlendingMode.Additive"/> would want <c>(One, One)</c>, and
    /// <see cref="BlendingMode.Multiply"/> and <see cref="BlendingMode.Screen"/> use per-channel destination
    /// factors that do not decompose so simply — so they keep the behaviour they have always had rather than
    /// gaining a half-correct translation. Their color is left straight to match.
    /// </para>
    /// </remarks>
    private static void drawComposedQuad(IRenderer renderer, IFrameBuffer buffer, RectangleF rect, Vector4 color, BlendingMode requested)
    {
        if (requested != BlendingMode.Alpha)
        {
            drawQuad(renderer, buffer, rect, color, requested);
            return;
        }

        var premultiplied = new Vector4(color.X * color.W, color.Y * color.W, color.Z * color.W, color.W);

        drawQuad(renderer, buffer, rect, premultiplied, BlendingMode.Premultiplied);
    }

    private static void drawQuad(IRenderer renderer, IFrameBuffer buffer, RectangleF rect, Vector4 color, BlendingMode blending)
    {
        Span<Vertex.Vertex> quad = stackalloc Vertex.Vertex[4];
        fillQuad(quad, rect, color);

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
        quad[0] = new Vertex.Vertex
        {
            Position = new Vector2(rect.X, rect.Y),
            Color = color,
            TexCoords = new Vector2(0, 1),
            ClipData = new Vector4(0, 0, -1, -1)
        };
        quad[1] = new Vertex.Vertex
        {
            Position = new Vector2(rect.X + rect.Width, rect.Y),
            Color = color,
            TexCoords = new Vector2(1, 1),
            ClipData = new Vector4(0, 0, -1, -1)
        };
        quad[2] = new Vertex.Vertex
        {
            Position = new Vector2(rect.X + rect.Width, rect.Y + rect.Height),
            Color = color,
            TexCoords = new Vector2(1, 0),
            ClipData = new Vector4(0, 0, -1, -1)
        };
        quad[3] = new Vertex.Vertex
        {
            Position = new Vector2(rect.X, rect.Y + rect.Height),
            Color = color,
            TexCoords = new Vector2(0, 0),
            ClipData = new Vector4(0, 0, -1, -1)
        };
    }
}
