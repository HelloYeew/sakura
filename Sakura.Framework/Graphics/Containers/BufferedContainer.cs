// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A container that renders its children into an offscreen framebuffer, then draws that
/// texture to the screen as a single quad.
/// <para>
/// Use it when the subtree should be treated as one flattened image: correct whole-subtree
/// transparency (fading a normal container fades each child individually, causing overlap
/// artifacts; fading a buffered container fades the composite), cheap redraw of complex but
/// static content via <see cref="CacheDrawnFrameBuffer"/>, and texture-space effects:
/// Gaussian blur (<see cref="BlurSigma"/>, <see cref="BlurRotation"/>), grayscale
/// (<see cref="GrayscaleStrength"/>), effect tint/blending/placement
/// (<see cref="EffectColor"/>, <see cref="EffectBlending"/>, <see cref="EffectPlacement"/>)
/// and glow/bloom-style compositing via <see cref="DrawOriginal"/>.
/// </para>
/// <para>
/// Content is implicitly clipped to this container's screen-space bounds (the buffer only
/// captures that region). The container's <see cref="Drawable.Color"/> tints the composite.
/// </para>
/// <remarks>
/// The framebuffers live in GPU memory for the lifetime of the process once created —
/// prefer pooling/reusing buffered containers over creating and discarding many of them.
/// Effects (blur/grayscale) currently run on the OpenGL renderer only.
/// </remarks>
/// </summary>
public partial class BufferedContainer : Container
{
    /// <summary>
    /// When true, the offscreen buffer is only re-rendered when something in the subtree
    /// changes (invalidation, topology, transforms) or <see cref="ForceRedraw"/> is called.
    /// When false (default), it is re-rendered every frame.
    /// </summary>
    public bool CacheDrawnFrameBuffer { get; set; }

    /// <summary>
    /// Whether the buffer is sampled with nearest-neighbour filtering when drawn to screen,
    /// snapping to whole pixels instead of bilinear smoothing. Useful for pixel-art content.
    /// Fixed at construction time (the underlying texture filter is baked into the buffer).
    /// </summary>
    public readonly bool PixelSnapping;

    private Vector2 frameBufferScale = Vector2.One;

    /// <summary>
    /// Resolution multiplier for the offscreen buffer relative to the on-screen size, per axis.
    /// Values below 1 trade sharpness for fill-rate/memory (e.g. 0.5 renders at half
    /// resolution and scales up); values above 1 supersample.
    /// </summary>
    public Vector2 FrameBufferScale
    {
        get => frameBufferScale;
        set
        {
            if (frameBufferScale.Equals(value))
                return;

            frameBufferScale = value;
            ForceRedraw();
        }
    }

    private Vector2 blurSigma = Vector2.Zero;

    /// <summary>
    /// Gaussian blur strength in logical pixels, in two orthogonal directions
    /// (X and Y when <see cref="BlurRotation"/> is zero). Zero (default) disables the blur
    /// passes. Combining a large sigma with a low <see cref="FrameBufferScale"/> is the
    /// cheap way to get heavy blurs (e.g. background blur behind menus).
    /// </summary>
    public Vector2 BlurSigma
    {
        get => blurSigma;
        set
        {
            if (blurSigma.Equals(value))
                return;

            blurSigma = value;
            ForceRedraw();
        }
    }

    private float blurRotation;

    /// <summary>
    /// Rotates the blur directions clockwise, in degrees. Has no visible effect when
    /// <see cref="BlurSigma"/> has the same magnitude in both directions.
    /// </summary>
    public float BlurRotation
    {
        get => blurRotation;
        set
        {
            if (blurRotation == value)
                return;

            blurRotation = value;
            ForceRedraw();
        }
    }

    private float grayscaleStrength;

    /// <summary>
    /// Desaturation amount applied as an effect pass: 0 = original colors (default),
    /// 1 = fully grayscale.
    /// </summary>
    public float GrayscaleStrength
    {
        get => grayscaleStrength;
        set
        {
            if (grayscaleStrength == value)
                return;

            grayscaleStrength = value;
            ForceRedraw();
        }
    }

    private bool drawOriginal;

    /// <summary>
    /// When true, the original (un-effected) buffer is drawn in addition to the effect
    /// result. Combined with an additive <see cref="EffectBlending"/> and a blur, this
    /// produces a glow; with <see cref="EffectPlacement.InFront"/> a "veil" over the original.
    /// Has no effect when no effect pass is active.
    /// </summary>
    public bool DrawOriginal
    {
        get => drawOriginal;
        set
        {
            if (drawOriginal == value)
                return;

            drawOriginal = value;
            ForceRedraw();
        }
    }

    private Color effectColor = Color.White;

    /// <summary>
    /// Multiplicative color of the effect result (e.g. the blurred image). Default white.
    /// Does not affect the original drawn when <see cref="DrawOriginal"/> is true.
    /// </summary>
    public Color EffectColor
    {
        get => effectColor;
        set
        {
            if (effectColor.Equals(value))
                return;

            effectColor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private BlendingMode? effectBlending;

    /// <summary>
    /// Blending used to draw the effect result. Null (default) inherits this container's
    /// <see cref="Drawable.Blending"/>. Use <see cref="BlendingMode.Additive"/> for glows.
    /// Does not affect the original drawn when <see cref="DrawOriginal"/> is true.
    /// </summary>
    public BlendingMode? EffectBlending
    {
        get => effectBlending;
        set
        {
            if (effectBlending == value)
                return;

            effectBlending = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private EffectPlacement effectPlacement = EffectPlacement.Behind;

    /// <summary>
    /// Whether the effect result is drawn behind (default) or in front of the original.
    /// Only relevant when <see cref="DrawOriginal"/> is true.
    /// </summary>
    public EffectPlacement EffectPlacement
    {
        get => effectPlacement;
        set
        {
            if (effectPlacement == value)
                return;

            effectPlacement = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    // Note: not Color.Transparent (which is white with zero alpha and would fringe to
    // white when children blend over it) — true transparent black.
    private Color backgroundColor = default;

    /// <summary>
    /// The color the buffer is cleared to before children render. Transparent black by default.
    /// </summary>
    public Color BackgroundColor
    {
        get => backgroundColor;
        set
        {
            if (backgroundColor.Equals(value))
                return;

            backgroundColor = value;
            ForceRedraw();
        }
    }

    /// <summary>
    /// Forces the buffer (and effects) to be re-rendered on the next draw.
    /// Only relevant when <see cref="CacheDrawnFrameBuffer"/> is true.
    /// </summary>
    public void ForceRedraw() => Invalidate(InvalidationFlags.DrawInfo);

    /// <summary>
    /// State shared between this drawable and its (triple-buffered) draw nodes, so all
    /// of them reuse one set of framebuffers. Owned and touched by the draw thread only.
    /// </summary>
    internal readonly BufferedContainerSharedData SharedData = new BufferedContainerSharedData();

    /// <param name="pixelSnapping">
    /// Whether the buffer should be sampled with nearest-neighbour filtering when drawn
    /// (see <see cref="PixelSnapping"/>).
    /// </param>
    public BufferedContainer(bool pixelSnapping = false)
    {
        PixelSnapping = pixelSnapping;
    }

    protected override DrawNode CreateDrawNode() => new BufferedContainerDrawNode();

    /// <summary>
    /// Draw-thread-owned shared state for <see cref="BufferedContainer"/>.
    /// </summary>
    internal sealed class BufferedContainerSharedData
    {
        /// <summary>
        /// The main offscreen buffer holding the original rendered content.
        /// Created and resized lazily on the draw thread.
        /// </summary>
        public IFrameBuffer? FrameBuffer;

        /// <summary>
        /// Ping-pong buffers for the effect passes. Only created when an effect is active.
        /// </summary>
        public readonly IFrameBuffer?[] EffectBuffers = new IFrameBuffer?[2];

        /// <summary>
        /// The buffer holding the final effect result (null when no effect is active),
        /// persisted so cached frames can composite without re-rendering.
        /// </summary>
        public IFrameBuffer? FinalEffectBuffer;

        /// <summary>
        /// The subtree version the buffer contents were last rendered against
        /// (used by <see cref="CacheDrawnFrameBuffer"/>).
        /// </summary>
        public long RenderedVersion = -1;
    }
}

/// <summary>
/// Whether a <see cref="BufferedContainer"/> effect is drawn behind or in front of the original.
/// </summary>
public enum EffectPlacement
{
    Behind,
    InFront,
}
