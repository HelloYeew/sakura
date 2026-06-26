// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;
using Sakura.Framework.Utilities;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A lowest level of the component hierarchy. All drawable components should be inherited from this class.
/// </summary>
public abstract partial class Drawable : IDependencyInjectionCandidate
{
    private static readonly GlobalStatistic<int> stat_updated_last_frame = GlobalStatistics.Get<int>("Drawables", "Updated Last Frame");
    private static readonly GlobalStatistic<int> stat_invalidations = GlobalStatistics.Get<int>("Drawables", "Invalidations");
    private static readonly GlobalStatistic<int> stat_draw_node_applied = GlobalStatistics.Get<int>("DrawNodes", "State Applied");
    private static readonly GlobalStatistic<int> stat_draw_node_reused = GlobalStatistics.Get<int>("DrawNodes", "State Reused (Clean)");

    private Container? parent;

    /// <summary>
    /// Monotonic sequence number assigned by the parent container on add, used as a stable
    /// tie-breaker when sorting children by depth.
    /// </summary>
    internal long ChildInsertionOrder;

    /// <summary>
    /// The last observed aliveness state, used by the parent to detect lifetime transitions
    /// (which change draw-tree membership without raising any invalidation).
    /// </summary>
    internal bool WasAlive = true;

    public Container? Parent
    {
        get => parent;
        internal set
        {
            if (parent == value) return;
            parent = value;
            OnParentChanged();
        }
    }

    /// <summary>
    /// Invoked when the parent of this drawable is changed (added or removed from a container).
    /// </summary>
    protected virtual void OnParentChanged() { }

    /// <summary>
    /// When true, this drawable will be disposed automatically when removed from its parent
    /// container via <see cref="Container.Remove"/> or <see cref="Container.Clear"/>.
    /// Set this on drawables that own unmanaged resources (e.g. video decoders, audio tracks).
    /// Defaults to false to preserve backward-compatible behavior.
    /// </summary>
    public bool DisposeOnRemoval { get; set; }

    public bool IsHovered { get; internal set; }
    public bool IsDragged { get; internal set; }

    /// <summary>
    /// The current loading state of the component
    /// </summary>
    public LoadState LoadState { get; internal set; } = LoadState.NotLoaded;

    /// <summary>
    /// Whether this drawable has been ready or loaded.
    /// </summary>
    public bool IsLoaded => LoadState >= LoadState.Ready;

    private readonly object loadLock = new object();
    private Task? loadTask;
    private int? loadTaskId;
    private IReadOnlyDependencyContainer? asyncParentDependencies;
    private bool isTopLevelAsyncLoad;

    private Anchor anchor = Anchor.TopLeft;
    private Anchor origin = Anchor.TopLeft;
    private Vector2 position = Vector2.Zero;
    private Vector2 size = Vector2.Zero;
    private Vector2 scale = Vector2.One;
    private Vector2 shear = Vector2.Zero;
    private float rotation;
    private Axes relativeSizeAxes = Axes.None;
    private Axes relativePositionAxes = Axes.None;
    private Color color = Color.White;
    private float alpha = 1f;
    private MarginPadding margin;
    private MarginPadding padding;
    private float depth;
    private bool alwaysPresent;
    private Texture? texture;
    private TextureFillMode fillMode = TextureFillMode.Stretch;
    private IFrameBasedClock? clock;
    private bool hasCustomClock;

    /// <summary>
    /// True if a clock was explicitly assigned to this drawable (via the <see cref="Clock"/> setter)
    /// as opposed to one inherited from a parent via <see cref="InheritClock"/>.
    /// </summary>
    protected bool HasCustomClock => hasCustomClock;

    /// <summary>
    /// The clock driving this drawable's time. By default this is the parent's clock,
    /// shared by reference all the way down the hierarchy (so all drawables report the
    /// same timeline). Assigning a clock explicitly marks it as custom: it is preserved
    /// when the drawable is added to a container, and if it is a <see cref="FramedClock"/>
    /// it is processed once per frame by <see cref="UpdateSubTree"/>.
    /// All times are in milliseconds.
    /// </summary>
    public virtual IFrameBasedClock Clock
    {
        get => clock ??= new Clock(true);
        set
        {
            if (clock == value) return;
            clock = value;
            hasCustomClock = true;
            OnClockChanged();
        }
    }

    /// <summary>
    /// Adopts the parent's clock by reference without marking it as custom.
    /// Pending transform and scheduler times are rebased onto the new clock's timeline
    /// so work scheduled before the drawable was added behaves as if scheduled at add time.
    /// </summary>
    internal void InheritClock(IFrameBasedClock parentClock)
    {
        if (hasCustomClock || ReferenceEquals(clock, parentClock))
            return;

        double oldTime = clock?.CurrentTime ?? parentClock.CurrentTime;
        clock = parentClock;

        double delta = parentClock.CurrentTime - oldTime;
        if (delta != 0)
        {
            if (transforms != null)
            {
                foreach (var t in transforms)
                {
                    t.StartTime += delta;
                    t.EndTime += delta;
                }
            }

            scheduler?.Rebase(delta);
        }

        OnClockChanged();
    }

    private Scheduler? scheduler;

    /// <summary>
    /// The scheduler for this drawable, used for delaying and scheduling tasks.
    /// Created lazily on first access; most drawables never need one.
    /// </summary>
    public Scheduler Scheduler => scheduler ??= new Scheduler(Clock);

    private List<Transform>? transforms;

    /// <summary>
    /// An internal property used by the transform extension methods to sequence animations.
    /// It represents the delay before the next transformation can begin.
    /// </summary>
    internal double TimeUntilTransformsCanStart { get; set; }

    public float DrawAlpha { get; private protected set; }

    /// <summary>
    /// An invalidation flag representing which aspects of the drawable need to be recomputed.
    /// </summary>
    protected internal InvalidationFlags Invalidation = InvalidationFlags.All;

    protected internal Vertex[] Vertices = new Vertex[4];

    /// <summary>
    /// How <see cref="Vertices"/> is interpreted by the renderer. The base drawable produces
    /// an indexed quad (4 vertices, ordered TL, TR, BR, BL); drawables that generate arbitrary
    /// triangle lists in <see cref="GenerateVertices"/> must override this to
    /// <see cref="VertexTopology.Triangles"/>.
    /// </summary>
    protected internal virtual VertexTopology Topology => VertexTopology.Quads;

    public Anchor Anchor
    {
        get => anchor;
        set
        {
            if (anchor == value) return;
            anchor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Anchor Origin
    {
        get => origin;
        set
        {
            if (origin == value) return;
            origin = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Vector2 Position
    {
        get => position;
        set
        {
            if (position == value) return;
            position = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public virtual Vector2 Size
    {
        get => size;
        set
        {
            if (size == value) return;
            size = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Vector2 Scale
    {
        get => scale;
        set
        {
            if (scale == value) return;
            scale = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The shear of this drawable.
    /// X represents the shear factor along the X-axis (relative to Y).
    /// Y represents the shear factor along the Y-axis (relative to X).
    /// </summary>
    public Vector2 Shear
    {
        get => shear;
        set
        {
            if (shear == value) return;
            shear = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public float Rotation
    {
        get => rotation;
        set
        {
            if (Math.Abs(rotation - value) < 0.0001f) return;
            rotation = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Axes RelativeSizeAxes
    {
        get => relativeSizeAxes;
        set
        {
            if (relativeSizeAxes == value) return;
            relativeSizeAxes = value;
            if ((relativeSizeAxes & Axes.X) != 0 && size.X == 0)
                size.X = 1;
            if ((relativeSizeAxes & Axes.Y) != 0 && size.Y == 0)
                size.Y = 1;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Axes RelativePositionAxes
    {
        get => relativePositionAxes;
        set
        {
            if (relativePositionAxes == value) return;
            relativePositionAxes = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Color Color
    {
        get => color;
        set
        {
            if (color == value) return;
            color = value;
            Invalidate(InvalidationFlags.Colour);
        }
    }

    public float Alpha
    {
        get => alpha;
        set
        {
            if (Math.Abs(alpha - value) < 0.0001f) return;

            bool wasVisible = alpha > 0;
            alpha = Math.Clamp(value, 0f, 1f);
            bool isVisible = alpha > 0;

            Invalidate(InvalidationFlags.Colour);

            // Visibility is a layout input: auto-size containers skip hidden children
            // (e.g. a dropdown grows only while its menu is shown), so crossing the
            // visible/hidden boundary must notify an interested parent.
            if (wasVisible != isVisible)
                Parent?.OnChildGeometryInvalidated();
        }
    }

    public float Depth
    {
        get => depth;
        set
        {
            if (Math.Abs(depth - value) < 0.0001f) return;
            depth = value;
            // Bumping the topology version re-sorts the parent's children (and thus draw order)
            // on the next frame; no geometry recomputation is required for a depth change.
            Parent?.InvalidateTopology();
        }
    }

    public MarginPadding Margin
    {
        get => margin;
        set
        {
            if (margin.Equals(value)) return;
            margin = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public MarginPadding Padding
    {
        get => padding;
        set
        {
            if (padding.Equals(value)) return;
            padding = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Whether this drawable should be update even when not present on the screen.
    /// </summary>
    public bool AlwaysPresent
    {
        get => alwaysPresent;
        set
        {
            if (alwaysPresent == value) return;
            alwaysPresent = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The texture applied to this drawable. (e.g. image)
    /// </summary>
    public Texture? Texture
    {
        get => texture;
        set
        {
            if (texture == value) return;
            texture = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The fill mode for the texture applied to this drawable.
    /// </summary>
    public TextureFillMode FillMode
    {
        get => fillMode;
        set
        {
            if (fillMode == value) return;
            fillMode = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The width of this drawable.
    /// </summary>
    public virtual float Width
    {
        get => Size.X;
        set => Size = new Vector2(value, Size.Y);
    }

    /// <summary>
    /// The height of this drawable.
    /// </summary>
    public virtual float Height
    {
        get => Size.Y;
        set => Size = new Vector2(Size.X, value);
    }

    /// <summary>
    /// The X component of the position of this drawable.
    /// </summary>
    public float X
    {
        get => Position.X;
        set => Position = new Vector2(value, Position.Y);
    }

    /// <summary>
    /// The Y component of the position of this drawable.
    /// </summary>
    public float Y
    {
        get => Position.Y;
        set => Position = new Vector2(Position.X, value);
    }

    /// <summary>
    /// The blending mode to use when drawing this drawable.
    /// </summary>
    public BlendingMode Blending { get; set; } = BlendingMode.Alpha;

    public virtual void Hide() => Alpha = 0f;
    public virtual void Show() => Alpha = 1f;
    public bool IsHidden => Alpha <= 0f;

    public RectangleF DrawRectangle { get; protected set; }
    public Vector2 DrawSize { get; private set; }

    /// <summary>
    /// The affine screen-space transform of this drawable. 2D rendering never needs a full
    /// 4x4 matrix; a 3x2 affine matrix is roughly half the storage and FLOPs per concatenation.
    /// </summary>
    public Matrix3x2 ModelMatrix = Matrix3x2.Identity;

    #region Calculation of Draw Info

    protected internal virtual void UpdateTransforms()
    {
        DrawAlpha = (Parent?.DrawAlpha ?? 1f) * Alpha;

        Matrix3x2 localMatrix;
        Vector2 finalDrawSize;
        Vector2 originVector = GetAnchorOriginVector(Origin);

        if (Parent == null)
        {
            // This is the root drawable.
            finalDrawSize = Size;

            // Transform order: Origin Translation -> Scale -> Shear -> Rotation -> Position Translation
            var m = Matrix3x2.CreateTranslation(-originVector.X, -originVector.Y); // Translate so origin is at (0,0)
            m *= Matrix3x2.CreateScale(finalDrawSize.X * Scale.X, finalDrawSize.Y * Scale.Y); // Scale around (0,0)

            if (Shear != Vector2.Zero)
            {
                var shearMatrix = Matrix3x2.Identity;
                shearMatrix.M21 = Shear.X; // X shear factor (x' = x + m21*y)
                shearMatrix.M12 = Shear.Y; // Y shear factor (y' = y + m12*x)
                m *= shearMatrix;
            }

            m *= Matrix3x2.CreateRotation((float)(Rotation * Math.PI / 180.0f)); // Rotate around (0,0)
            m *= Matrix3x2.CreateTranslation(Position.X, Position.Y); // Translate to final position

            ModelMatrix = m;

            DrawSize = finalDrawSize;
        }
        else
        {
            // This is a child drawable

            // Final pixel size of the parent's content area.
            Vector2 parentChildSize = Parent.ChildSize;
            Vector2 parentDrawSize = Parent.DrawSize;

            // prevent division by zero for drawables with no area.
            if (parentChildSize.X == 0)
                parentChildSize.X = 1;
            if (parentChildSize.Y == 0)
                parentChildSize.Y = 1;
            if (parentDrawSize.X == 0)
                parentDrawSize.X = 1;
            if (parentDrawSize.Y == 0)
                parentDrawSize.Y = 1;

            // Calculate scale relative to parent
            finalDrawSize = Size;
            if ((RelativeSizeAxes & Axes.X) != 0)
                finalDrawSize.X = Math.Max(0, parentChildSize.X * Size.X - Margin.Total.X);
            if ((RelativeSizeAxes & Axes.Y) != 0)
                finalDrawSize.Y = Math.Max(0, parentChildSize.Y * Size.Y - Margin.Total.Y);

            // Calculate position relative to parent
            Vector2 pixelPosition = Position;
            if ((RelativePositionAxes & Axes.X) != 0)
                pixelPosition.X *= parentChildSize.X;
            if ((RelativePositionAxes & Axes.Y) != 0)
                pixelPosition.Y *= parentChildSize.Y;

            Vector2 anchorOffset = GetAnchorOriginVector(Anchor);
            pixelPosition.X += anchorOffset.X * parentChildSize.X;
            pixelPosition.Y += anchorOffset.Y * parentChildSize.Y;

            // Apply margin based on the anchor offset
            pixelPosition.X += Margin.Left - anchorOffset.X * Margin.Total.X;
            pixelPosition.Y += Margin.Top - anchorOffset.Y * Margin.Total.Y;

            // Shift by Parent's Padding to get position relative to Parent's Top-Left (DrawSize space)
            pixelPosition.X += Parent.Padding.Left;
            pixelPosition.Y += Parent.Padding.Top;

            var m = Matrix3x2.CreateTranslation(-originVector.X, -originVector.Y);
            m *= Matrix3x2.CreateScale(finalDrawSize.X * Scale.X, finalDrawSize.Y * Scale.Y);

            if (Shear != Vector2.Zero)
            {
                var shearMatrix = Matrix3x2.Identity;
                shearMatrix.M21 = Shear.X; // X shear factor
                shearMatrix.M12 = Shear.Y; // Y shear factor
                m *= shearMatrix;
            }

            m *= Matrix3x2.CreateRotation((float)(Rotation * Math.PI / 180.0f));
            m *= Matrix3x2.CreateTranslation(pixelPosition.X, pixelPosition.Y);

            // Normalize to Parent's 0..1 space so Parent.ModelMatrix applies correctly
            m *= Matrix3x2.CreateScale(1.0f / parentDrawSize.X, 1.0f / parentDrawSize.Y);

            localMatrix = m;
            ModelMatrix = localMatrix * Parent.ModelMatrix;
        }

        DrawSize = finalDrawSize;

        GenerateVertices();
    }

    protected virtual void GenerateVertices()
    {
        float rLinear = ColorExtensions.SrgbToLinear(Color.R);
        float gLinear = ColorExtensions.SrgbToLinear(Color.G);
        float bLinear = ColorExtensions.SrgbToLinear(Color.B);

        float colorAlpha = Color.A / 255f;

        var calculatedColor = new Vector4(rLinear, gLinear, bLinear, DrawAlpha * colorAlpha);

        // Default UVs (0 to 1)
        var uvRect = Texture?.UvRect ?? new RectangleF(0, 0, 1, 1);
        Vector2 uvTopLeft = new Vector2(uvRect.X, uvRect.Y);
        Vector2 uvBottomRight = new Vector2(uvRect.X + uvRect.Width, uvRect.Y + uvRect.Height);

        // Default draw area (0 to 1 in local space)
        Vector2 drawTopLeft = Vector2.Zero;
        Vector2 drawBottomRight = Vector2.One;

        // Apply fill mode logic if we have a texture
        if (Texture != null && DrawSize.X > 0 && DrawSize.Y > 0)
        {
            float textureAspect = (float)Texture.Width / Texture.Height;
            float drawAspect = DrawSize.X / DrawSize.Y;

            switch (FillMode)
            {
                case TextureFillMode.Fit:
                    // Shrink the draw area to fit the aspect ratio (Letterboxing)
                    if (textureAspect > drawAspect)
                    {
                        // Texture is wider: Fit width, center height
                        float localScale = drawAspect / textureAspect;
                        float offset = (1.0f - localScale) / 2.0f;
                        drawTopLeft.Y = offset;
                        drawBottomRight.Y = 1.0f - offset;
                    }
                    else
                    {
                        // Texture is taller: Fit height, center width
                        float localScale = textureAspect / drawAspect;
                        float offset = (1.0f - localScale) / 2.0f;
                        drawTopLeft.X = offset;
                        drawBottomRight.X = 1.0f - offset;
                    }
                    break;

                case TextureFillMode.Fill:
                    // Shrink the UVs to crop the image (Zoom)
                    if (textureAspect > drawAspect)
                    {
                        // Texture is wider: Crop X
                        float visibleWidthRatio = drawAspect / textureAspect;
                        float uvOffset = (1.0f - visibleWidthRatio) / 2.0f;
                        float uvWidth = uvBottomRight.X - uvTopLeft.X;
                        uvTopLeft.X += uvWidth * uvOffset;
                        uvBottomRight.X -= uvWidth * uvOffset;
                    }
                    else
                    {
                        // Texture is taller: Crop Y
                        float visibleHeightRatio = textureAspect / drawAspect;
                        float uvOffset = (1.0f - visibleHeightRatio) / 2.0f;
                        float uvHeight = uvBottomRight.Y - uvTopLeft.Y;
                        uvTopLeft.Y += uvHeight * uvOffset;
                        uvBottomRight.Y -= uvHeight * uvOffset;
                    }
                    break;

                case TextureFillMode.Tile:
                    // Expand UVs > 1.0 to repeat
                    float repeatX = DrawSize.X / Texture.Width;
                    float repeatY = DrawSize.Y / Texture.Height;

                    uvBottomRight.X = uvTopLeft.X + (uvRect.Width * repeatX);
                    uvBottomRight.Y = uvTopLeft.Y + (uvRect.Height * repeatY);
                    break;
            }
        }

        // Apply model matrix to the calculated local draw area
        var vTopLeft = Vector2.Transform(new Vector2(drawTopLeft.X, drawTopLeft.Y), ModelMatrix);
        var vTopRight = Vector2.Transform(new Vector2(drawBottomRight.X, drawTopLeft.Y), ModelMatrix);
        var vBottomLeft = Vector2.Transform(new Vector2(drawTopLeft.X, drawBottomRight.Y), ModelMatrix);
        var vBottomRight = Vector2.Transform(new Vector2(drawBottomRight.X, drawBottomRight.Y), ModelMatrix);

        Vertices[0] = new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = uvTopLeft, Color = calculatedColor };
        Vertices[1] = new Vertex { Position = new Vector2(vTopRight.X, vTopRight.Y), TexCoords = new Vector2(uvBottomRight.X, uvTopLeft.Y), Color = calculatedColor };
        Vertices[2] = new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = uvBottomRight, Color = calculatedColor };
        Vertices[3] = new Vertex { Position = new Vector2(vBottomLeft.X, vBottomLeft.Y), TexCoords = new Vector2(uvTopLeft.X, uvBottomRight.Y), Color = calculatedColor };

        // Calculate DrawRectangle in screen space
        float minX = Math.Min(vTopLeft.X, Math.Min(vTopRight.X, Math.Min(vBottomLeft.X, vBottomRight.X)));
        float minY = Math.Min(vTopLeft.Y, Math.Min(vTopRight.Y, Math.Min(vBottomLeft.Y, vBottomRight.Y)));
        float maxX = Math.Max(vTopLeft.X, Math.Max(vTopRight.X, Math.Max(vBottomLeft.X, vBottomRight.X)));
        float maxY = Math.Max(vTopLeft.Y, Math.Max(vTopRight.Y, Math.Max(vBottomLeft.Y, vBottomRight.Y)));

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    public bool Contains(Vector2 screenSpacePos)
    {
        // Fast rejection against the screen-space AABB.
        if (!DrawRectangle.Contains(screenSpacePos))
            return false;

        // The AABB over-covers rotated/sheared drawables (including children of rotated
        // ancestors), so confirm via the exact inverse transform into local 0..1 space.
        // For axis-aligned drawables the local test passes trivially.
        if (Matrix3x2.Invert(ModelMatrix, out var inverse))
        {
            var local = Vector2.Transform(screenSpacePos, inverse);

            // Allow half a pixel of leniency so edge coordinates (e.g. X + Width, which the
            // inclusive AABB accepts) aren't rejected by floating-point error in the
            // round-trip through the inverse transform.
            float epsX = DrawSize.X > 0 ? 0.5f / DrawSize.X : 0.001f;
            float epsY = DrawSize.Y > 0 ? 0.5f / DrawSize.Y : 0.001f;

            return local.X >= -epsX && local.X <= 1 + epsX &&
                   local.Y >= -epsY && local.Y <= 1 + epsY;
        }

        // Degenerate (non-invertible) transform: fall back to the AABB result.
        return true;
    }

    /// <summary>
    /// Converts a screen-space position into this drawable's local pixel space.
    /// The result is in the same coordinate system as <see cref="Position"/> and <see cref="DrawSize"/>.
    /// Falls back to a simple AABB offset when the transform is non-invertible.
    /// </summary>
    public Vector2 ToLocalSpace(Vector2 screenSpacePos)
    {
        if (Matrix3x2.Invert(ModelMatrix, out var inverse))
        {
            var normalised = Vector2.Transform(screenSpacePos, inverse);
            return new Vector2(normalised.X * DrawSize.X, normalised.Y * DrawSize.Y);
        }

        return screenSpacePos - new Vector2(DrawRectangle.X, DrawRectangle.Y);
    }

    /// <summary>
    /// Converts a local pixel-space position into screen space.
    /// </summary>
    public Vector2 ToScreenSpace(Vector2 localPos)
    {
        var normalised = DrawSize.X > 0 && DrawSize.Y > 0
            ? new Vector2(localPos.X / DrawSize.X, localPos.Y / DrawSize.Y)
            : localPos;

        return Vector2.Transform(normalised, ModelMatrix);
    }

    public static Vector2 GetAnchorOriginVector(Anchor anchor)
    {
        switch (anchor)
        {
            case Anchor.TopLeft: return new Vector2(0.0f, 0.0f);
            case Anchor.TopCentre: return new Vector2(0.5f, 0.0f);
            case Anchor.TopRight: return new Vector2(1.0f, 0.0f);
            case Anchor.CentreLeft: return new Vector2(0.0f, 0.5f);
            case Anchor.Centre: return new Vector2(0.5f, 0.5f);
            case Anchor.CentreRight: return new Vector2(1.0f, 0.5f);
            case Anchor.BottomLeft: return new Vector2(0.0f, 1.0f);
            case Anchor.BottomCentre: return new Vector2(0.5f, 1.0f);
            case Anchor.BottomRight: return new Vector2(1.0f, 1.0f);
            default: return Vector2.Zero;
        }
    }

    #endregion

    // Attribute reflection is too slow to run on every load (e.g. when spawning pooled
    // gameplay objects), so the result is cached per type.
    private static readonly ConcurrentDictionary<Type, bool> long_running_type_cache = new ConcurrentDictionary<Type, bool>();

    private static bool isLongRunningType(Type type)
        => long_running_type_cache.GetOrAdd(type, static t => t.GetCustomAttribute<LongRunningLoadAttribute>(true) != null);

    /// <summary>
    /// Called to perform loading tasks to load required resources and dependencies.
    /// Called once before the first update.
    /// This method is recursively called down the drawable hierarchy.
    /// </summary>
    public virtual void Load()
    {
        if (LoadState >= LoadState.Ready) return;

        // If an asynchronous load is currently running, block the thread until it finishes
        // The loadTaskId check ensures we don't deadlock if the Task itself is the one calling Load()
        if (loadTask != null && loadTaskId != Task.CurrentId && !loadTask.IsCompleted)
        {
            loadTask.Wait();
            return;
        }

        lock (loadLock)
        {
            if (LoadState >= LoadState.Ready) return;

            bool isLongRunning = isLongRunningType(GetType());
            if (isLongRunning && !isTopLevelAsyncLoad)
            {
                throw new InvalidOperationException(
                    $"Drawable {GetDisplayName()} is marked with [LongRunningLoad] and must exclusively be loaded via {nameof(LoadComponentAsync)}().");
            }

            LoadState = LoadState.Loading;

            loadDependencies(asyncParentDependencies);
            asyncParentDependencies = null;

            OnLoad(this);

            LoadState = LoadState.Ready;
        }
    }

    /// <summary>
    /// Asynchronously loads a component and its children, preventing interface thread blockage.
    /// </summary>
    /// <param name="component">The component to load.</param>
    /// <param name="onLoaded">An action to perform once the component is ready (e.g., adding it to a container).</param>
    /// <param name="cancellationToken">A token to cancel the task before it executes.</param>
    /// <returns>A task representing the load process.</returns>
    public Task LoadComponentAsync<T>(T component, Action<T>? onLoaded = null, CancellationToken cancellationToken = default) where T : Drawable
    {
        ArgumentNullException.ThrowIfNull(component);

        lock (component.loadLock)
        {
            // if the component is already loading or loaded, attach to the existing task.
            if (component.LoadState >= LoadState.Loading || component.loadTask != null)
            {
                if (onLoaded != null)
                {
                    if (component.loadTask != null && !component.loadTask.IsCompleted)
                        return component.loadTask.ContinueWith(_ => Scheduler?.Add(() => onLoaded(component)), cancellationToken);

                    Scheduler?.Add(() => onLoaded(component));
                }
                return component.loadTask ?? Task.CompletedTask;
            }

            // transfer our dependencies down so the child can resolve dependencies off-thread.
            component.asyncParentDependencies = Dependencies;
            component.isTopLevelAsyncLoad = true;

            bool isLongRunning = isLongRunningType(component.GetType());
            var creationOptions = isLongRunning ? TaskCreationOptions.LongRunning : TaskCreationOptions.None;
            var parentScheduler = Scheduler; // capture the parent's scheduler

            component.loadTask = Task.Factory.StartNew(() =>
            {
                component.loadTaskId = Task.CurrentId;
                component.Load(); // trigger the virtual method
            }, cancellationToken, creationOptions, TaskScheduler.Default);

            // return a continuation that handles routing the callback back to the main update thread
            return component.loadTask.ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception!.InnerException!;

                if (onLoaded != null && !cancellationToken.IsCancellationRequested)
                {
                    // the callback must be executed on the target's update thread.
                    parentScheduler.Add(() => onLoaded(component));
                }
            }, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Called after the drawable and all its children have been loaded.
    /// This method is recursively called down the drawable hierarchy.
    /// </summary>
    public virtual void LoadComplete()
    {
        if (LoadState >= LoadState.Loaded)
            return;

        LoadState = LoadState.Loaded;
        OnLoadComplete(this);
    }

    protected internal long DrawNodeInvalidationId { get; private set; } = 1;
    private readonly DrawNode?[] drawNodes = new DrawNode?[3];
    private DrawNode? drawNode;

    protected virtual DrawNode CreateDrawNode() => new DrawNode();

    public DrawNode GenerateDrawNode(int frameIndex)
    {
        drawNodes[frameIndex] ??= CreateDrawNode();
        var node = drawNodes[frameIndex]!;
        node.ApplyState(this);
        return node;
    }

    public virtual DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        drawNodes[frameIndex] ??= CreateDrawNode();
        var node = drawNodes[frameIndex]!;

        // Only apply state if the drawable has been invalidated since last generation
        if (node.InvalidationID != DrawNodeInvalidationId)
        {
            node.ApplyState(this);
            node.InvalidationID = DrawNodeInvalidationId;
            stat_draw_node_applied.Value++;
        }
        else
        {
            stat_draw_node_reused.Value++;
        }

        return node;
    }

    /// <summary>
    /// Set when this drawable's own geometry inputs (position, size, rotation, parent geometry, ...)
    /// have changed — as opposed to merely being flagged for a layout pass by a child notification.
    /// Containers use this to decide whether their children's matrices need recomputation.
    /// </summary>
    protected internal bool OwnGeometryInvalidated;

    /// <summary>
    /// Marks all or part of this drawable as requiring re-computation.
    /// A <see cref="InvalidationFlags.DrawInfo"/> invalidation means this drawable's own geometry
    /// changed; parents are only notified when they declare interest (auto-size / flow layouts),
    /// which keeps a single moving drawable from re-invalidating the whole tree.
    /// </summary>
    /// <param name="flags">An <see cref="InvalidationFlags"/> flag representing which aspects of the drawable need to be recomputed.</param>
    /// <param name="propagateToParent">Whether this invalidation should also notify this drawable's parent.</param>
    public virtual void Invalidate(InvalidationFlags flags = InvalidationFlags.All, bool propagateToParent = true)
    {
        bool dirtiesGeometry = (flags & InvalidationFlags.DrawInfo) != 0;

        // The parent must be notified even when this drawable is already dirty itself.
        // A drawable that measures and resizes itself during its own update pass (e.g.
        // SpriteText computing its layout) carries set flags at that moment — but its
        // parent may have already finished its layout this frame and still needs to learn
        // about the change. The notification receiver is idempotent, so this is cheap.
        if (dirtiesGeometry && propagateToParent)
            Parent?.OnChildGeometryInvalidated();

        // Already invalidated in all requested ways — nothing else to do.
        if ((Invalidation & flags) == flags && (!dirtiesGeometry || OwnGeometryInvalidated))
            return;

        stat_invalidations.Value++;

        Invalidation |= flags;

        if ((flags & (InvalidationFlags.DrawInfo | InvalidationFlags.Colour)) != 0)
        {
            DrawNodeInvalidationId++;

            // The drawn state changed, so every ancestor's cached draw-node subtree is stale.
            Parent?.MarkSubtreeDrawStateDirty();
        }

        if (dirtiesGeometry)
            OwnGeometryInvalidated = true;
    }

    /// <summary>
    /// Flags this drawable as requiring an update/layout pass without treating its own geometry
    /// as changed. Used when a child notifies an interested parent: the parent re-runs layout,
    /// and only if that layout actually changes its geometry (e.g. auto-size changes Size) does a
    /// real invalidation cascade to its children.
    /// </summary>
    protected internal void InvalidateLayout()
    {
        if ((Invalidation & InvalidationFlags.DrawInfo) != 0)
            return;

        stat_invalidations.Value++;
        Invalidation |= InvalidationFlags.DrawInfo;
        DrawNodeInvalidationId++;
        Parent?.MarkSubtreeDrawStateDirty();
    }

    #region Dependency Injection

    private IReadOnlyDependencyContainer dependencies = null!;
    private IReadOnlyDependencyContainer? ownDependencies;

    /// <summary>
    /// Provides access to this drawable's dependency container.
    /// Dependencies are resolved by walking up the drawable hierarchy.
    /// </summary>
    protected IReadOnlyDependencyContainer Dependencies => dependencies;

    /// <summary>
    /// Caches a dependency instance of the specified type in this drawable's own dependency container.
    /// Making it available to this drawable and its children.
    /// This should be called within a method marked with <see cref="BackgroundDependencyLoaderAttribute"/>
    /// </summary>
    /// <param name="instance">The instance to cache</param>
    /// <typeparam name="T">The type of the dependency to cache</typeparam>
    protected void Cache<T>(T instance) where T : class
    {
        if (ownDependencies is not DependencyContainer dc)
            throw new InvalidOperationException($"Cannot cache dependencies before {nameof(Load)} has been called.");

        dc.Cache(instance);
    }

    private void loadDependencies(IReadOnlyDependencyContainer? overrideParentContainer = null)
    {
        IReadOnlyDependencyContainer? parentContainer = overrideParentContainer ?? getParentDependencyContainer();

        // Build the child container, caching any [Cached] members declared on this drawable.
        // Uses the source-generated fast path when available, reflection fallback otherwise.
        dependencies = ownDependencies = DependencyActivator.BuildChildDependencies(this, parentContainer);

        // Inject [Resolved] members and invoke [BackgroundDependencyLoader].
        DependencyActivator.Inject(this, dependencies);
    }

    private IReadOnlyDependencyContainer? getParentDependencyContainer()
    {
        Drawable? p = Parent;
        while (p != null)
        {
            if (p.ownDependencies != null)
                return p.ownDependencies;
            p = p.Parent;
        }
        return null;
    }

    #endregion

    /// <summary>
    /// Whether this drawable is currently fully outside the masking bounds of its parents.
    /// </summary>
    public bool IsMaskedAway { get; internal set; }

    /// <summary>
    /// The inherited masking bounds from parent containers.
    /// </summary>
    protected internal RectangleF? CurrentMaskingBounds { get; set; }

    /// <summary>
    /// The entry point for the update recursion.
    /// </summary>
    public virtual void UpdateSubTree()
    {
        // inherited clocks are shared by reference and processed once by their owner
        // (e.g. the update thread); only an explicitly-assigned framed clock is ours to process.
        if (hasCustomClock && clock is FramedClock framedClock)
            framedClock.ProcessFrame();

        if ((IsMaskedAway || !IsAlive) && !AlwaysPresent)
            return;

        // Scheduler tasks and transforms must run BEFORE Update() so that any invalidations
        // they raise (e.g. a FadeIn/MoveTo on a container changing Alpha/Position) are visible
        // to Update's dirty-state checks. Container.Update captures these flags to decide
        // whether to cascade to children — raising them mid-Update would lose that cascade.
        if (IsLoaded)
        {
            scheduler?.Update();
            applyTransforms();
        }

        Update();
    }

    public virtual void Update()
    {
        if (!IsLoaded) return;

        stat_updated_last_frame.Value++;

        if (Invalidation == InvalidationFlags.None)
            return;

        if (!AlwaysPresent && Precision.AlmostEqualZero(Alpha))
        {
            DrawAlpha = 0;
            // Keep pending geometry invalidation (and the own-geometry marker) so a drawable
            // that moved while hidden recomputes — and cascades to children — once shown again.
            Invalidation &= ~InvalidationFlags.Colour;
            return;
        }

        if ((Invalidation & InvalidationFlags.DrawInfo) != 0)
        {
            UpdateTransforms();
        }
        else if ((Invalidation & InvalidationFlags.Colour) != 0)
        {
            // Colour-only change (fades are the common case in gameplay): rewrite the
            // vertex colours without redoing matrix and vertex-position work.
            UpdateDrawColour();
        }

        Invalidation = InvalidationFlags.None;
        OwnGeometryInvalidated = false;
    }

    /// <summary>
    /// Recomputes <see cref="DrawAlpha"/> and rewrites the color of the existing vertices
    /// without regenerating geometry. Called for color-only invalidations.
    /// Drawables whose vertices carry non-uniform colors must override this
    /// (falling back to <see cref="UpdateTransforms"/> is always correct).
    /// </summary>
    protected virtual void UpdateDrawColour()
    {
        DrawAlpha = (Parent?.DrawAlpha ?? 1f) * Alpha;

        float rLinear = ColorExtensions.SrgbToLinear(Color.R);
        float gLinear = ColorExtensions.SrgbToLinear(Color.G);
        float bLinear = ColorExtensions.SrgbToLinear(Color.B);
        var calculatedColor = new Vector4(rLinear, gLinear, bLinear, DrawAlpha * (Color.A / 255f));

        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i].Color = calculatedColor;
    }

    /// <summary>
    /// Invoked when the <see cref="Clock"/> of this drawable changes or reassigned.
    /// </summary>
    protected virtual void OnClockChanged()
    {
        scheduler?.SetClock(Clock);
    }

    #region Transformation Management

    private void applyTransforms()
    {
        if (transforms == null || transforms.Count == 0)
            return;

        double currentTime = Clock.CurrentTime;

        // First pass (forward): apply all active transforms in order so later transforms
        // in a sequence correctly overwrite earlier ones.
        for (int i = 0; i < transforms.Count; i++)
        {
            var t = transforms[i];
            if (currentTime >= t.StartTime)
                t.Apply(this, currentTime);
        }

        // Second pass (backward): remove completed transforms. We go backward so removals
        // don't shift indices of unprocessed entries. Final-value application already
        // happened in the forward pass above (GetEasedProgress clamps to 1 at completion).
        for (int i = transforms.Count - 1; i >= 0; i--)
        {
            var t = transforms[i];

            if (!t.IsLooping && currentTime >= t.EndTime)
            {
                transforms.RemoveAt(i);
            }
            else if (t.IsLooping && t.IsLoopComplete(currentTime))
            {
                transforms.RemoveAt(i);
            }
        }
    }

    internal void AddTransform(Transform transform)
    {
        // don't sort here for performance, looping backwards in ApplyTransforms handles completed transforms
        (transforms ??= new List<Transform>()).Add(transform);
    }

    /// <summary>
    /// Removes a specific transform from this drawable, if present.
    /// </summary>
    internal void RemoveTransform(Transform transform)
    {
        transforms?.Remove(transform);
    }

    /// <summary>
    /// Number of transforms currently registered on this drawable.
    /// Used by <see cref="TransformSequence{T}"/> to detect newly added transforms.
    /// </summary>
    internal int TransformCount => transforms?.Count ?? 0;

    /// <summary>
    /// Copies all transforms added after index <paramref name="countBefore"/> into <paramref name="result"/>.
    /// Used by <see cref="TransformSequence{T}"/> to track which transforms belong to a sequence.
    /// </summary>
    internal void CollectTransformsSince(int countBefore, List<Transform> result)
    {
        if (transforms == null) return;
        for (int i = countBefore; i < transforms.Count; i++)
            result.Add(transforms[i]);
    }

    /// <summary>
    /// Gets the end time of the latest-finishing transformation on this drawable.
    /// </summary>
    public double GetLatestTransformEndTime()
    {
        if (transforms == null || transforms.Count == 0)
            return Clock.CurrentTime;

        double latest = double.MinValue;
        for (int i = 0; i < transforms.Count; i++)
        {
            if (transforms[i].EndTime > latest)
                latest = transforms[i].EndTime;
        }
        return latest;
    }

    /// <summary>
    /// Removes all transformations from this drawable.
    /// </summary>
    /// <param name="propagateToChildren">Whether to also clear transformations on all children.</param>
    public void ClearTransforms(bool propagateToChildren = false)
    {
        transforms?.Clear();
        TimeUntilTransformsCanStart = 0;

        if (propagateToChildren && this is Container c)
        {
            foreach (var child in c.Children)
                child.ClearTransforms(true);
        }
    }

    internal void LoopLatestTransforms(int count = 0, double pause = 0)
    {
        if (transforms == null || transforms.Count == 0)
            return;

        double latestEndTime = GetLatestTransformEndTime();

        foreach (var t in transforms)
        {
            if (Precision.AlmostEquals(t.EndTime, latestEndTime))
            {
                t.IsLooping = true;
                t.LoopCount = count;
                t.LoopPause = pause;
            }
        }
    }

    /// <summary>
    /// Instantly finishes all transformations, applying their final values.
    /// </summary>
    /// <param name="propagateToChildren">Whether to also finish transformations on all children.</param>
    public void FinishTransforms(bool propagateToChildren = false)
    {
        if (transforms != null)
        {
            foreach (var t in transforms)
                t.Apply(this, t.EndTime);
        }

        ClearTransforms(propagateToChildren);
    }

    #endregion

    #region Lifetime Management

    /// <summary>
    /// The time at which this drawable becomes alive.
    /// </summary>
    public double LifetimeStart { get; set; } = double.MinValue;

    /// <summary>
    /// The time at which this drawable ceases to be alive (dead).
    /// </summary>
    public double LifetimeEnd { get; set; } = double.MaxValue;

    /// <summary>
    /// Whether this drawable should be removed from its parent when <see cref="IsAlive"/> is false.
    /// </summary>
    public bool RemoveWhenNotAlive { get; set; } = true;

    /// <summary>
    /// Whether the drawable is currently alive based on its lifetime and current <see cref="Clock"/> time.
    /// </summary>
    public bool IsAlive => Clock.CurrentTime >= LifetimeStart && Clock.CurrentTime < LifetimeEnd;

    /// <summary>
    /// Sets the lifetime end to the current time, or the end of the latest transform. (Whichever is later)
    /// This effectively marks the drawable for removal once all transforms have completed.
    /// </summary>
    public void Expire()
    {
        LifetimeEnd = Math.Max(Clock.CurrentTime, GetLatestTransformEndTime());
    }

    #endregion

    #region Event Handlers

    public virtual bool OnMouseDown(MouseButtonEvent e)
    {
        if (AcceptsFocus)
            GetContainingFocusManager()?.ChangeFocus(this);

        bool handled = false;

        if (e.Clicks >= 3)
            handled = OnTripleClick(e);
        else if (e.Clicks == 2)
            handled = OnDoubleClick(e);
        else if (e.Clicks == 1)
            handled = OnClick(e);

        if (e.Button == MouseButton.Left)
        {
            IsDragged = true;
            handled |= OnDragStart(e);
        }

        return handled;
    }

    public virtual bool OnMouseUp(MouseButtonEvent e)
    {
        if (IsDragged)
        {
            IsDragged = false;
            return OnDragEnd(e);
        }

        return false;
    }

    public virtual bool OnClick(MouseButtonEvent e) => false;
    public virtual bool OnDoubleClick(MouseButtonEvent e) => false;
    public virtual bool OnTripleClick(MouseButtonEvent e) => false;

    public virtual bool OnMouseMove(MouseEvent e)
    {
        if (IsDragged)
            return OnDrag(e);

        if (!IsHovered && Contains(e.ScreenSpaceMousePosition))
        {
            IsHovered = true;
            return OnHover(e);
        }

        if(IsHovered && !Contains(e.ScreenSpaceMousePosition))
        {
            IsHovered = false;
            return OnHoverLost(e);
        }

        return false;
    }

    public virtual bool OnHover(MouseEvent e) => false;
    public virtual bool OnHoverLost(MouseEvent e) => false;

    public virtual bool OnDragStart(MouseButtonEvent e) => false;
    public virtual bool OnDrag(MouseEvent e) => false;
    public virtual bool OnDragEnd(MouseButtonEvent e) => false;

    public virtual bool OnScroll(ScrollEvent e) => false;

    public virtual bool OnKeyDown(KeyEvent e) => false;
    public virtual bool OnKeyUp(KeyEvent e) => false;

    public virtual bool OnDragDropFile(DragDropFileEvent e) => false;
    public virtual bool OnDragDropText(DragDropTextEvent e) => false;

    public virtual bool OnTextInput(TextInputEvent e) => false;
    public virtual bool OnTextEditing(TextEditingEvent e) => false;

    public virtual bool OnGamepadButtonDown(GamepadButtonEvent e) => false;
    public virtual bool OnGamepadButtonUp(GamepadButtonEvent e) => false;

    public virtual bool OnGamepadAxisMotion(GamepadAxisEvent e) => false;

    public virtual void OnGamepadConnected(GamepadConnectedEvent e) { }
    public virtual void OnGamepadDisconnected(GamepadDisconnectedEvent e) { }

    #endregion

    #region Input queue opt-ins

    /// <summary>
    /// Whether this drawable should be considered for the non-positional input queue (keyboard,
    /// text, gamepad). Override to <c>false</c> to opt a subtree out of non-positional input.
    /// </summary>
    public virtual bool HandleNonPositionalInput => true;

    /// <summary>
    /// Whether this drawable should be considered for the positional input queue (mouse, scroll,
    /// drag, hover). Override to <c>false</c> to opt a subtree out of positional input.
    /// </summary>
    public virtual bool HandlePositionalInput => true;

    /// <summary>
    /// Whether this drawable receives positional input at the given screen-space point. Defaults to
    /// <see cref="Contains"/> so behaviour matches the existing hit test; override to widen or
    /// narrow the receptive area (e.g. an invisible drag handle larger than its visual bounds).
    /// </summary>
    /// <param name="screenSpacePos">The screen-space point to test.</param>
    public virtual bool ReceivePositionalInputAt(Vector2 screenSpacePos) => Contains(screenSpacePos);

    #endregion

    #region Focus Management

    /// <summary>
    /// Whether this drawable can currently accept keyboard focus.
    /// </summary>
    public virtual bool AcceptsFocus => false;

    /// <summary>
    /// Whether this drawable wants to automatically grab focus when possible.
    /// </summary>
    public virtual bool RequestsFocus => false;

    /// <summary>
    /// Whether this drawable currently holds the keyboard focus.
    /// </summary>
    public bool HasFocus { get; internal set; }

    /// <summary>
    /// Walks up the parent hierarchy to find the nearest FocusManager.
    /// </summary>
    protected internal IFocusManager? GetContainingFocusManager()
    {
        Drawable? p = Parent;
        while (p != null)
        {
            // Check if the parent implements the interface
            if (p is IFocusManager focusManager)
                return focusManager;

            p = p.Parent;
        }
        return null;
    }

    /// <summary>
    /// Walks up the parent hierarchy to find the nearest <see cref="IInputManagerProvider"/>
    /// (typically the app root), exposing the shared <see cref="InputManager"/> / authoritative
    /// <see cref="InputState"/>. Returns null when not yet attached under one.
    /// </summary>
    protected internal InputManager? GetContainingInputManager()
    {
        Drawable? p = Parent;
        while (p != null)
        {
            if (p is IInputManagerProvider provider)
                return provider.InputManager;

            p = p.Parent;
        }
        return null;
    }

    public virtual void OnFocus(FocusEvent e) { }
    public virtual void OnFocusLost(FocusLostEvent e) { }

    #endregion

    #region Event Hooks

    public event Action<Drawable> OnLoad = delegate { };
    public event Action<Drawable> OnLoadComplete = delegate { };

    #endregion

    #region Naming

    private Guid? internalId;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Get a display name for this drawable, including its type and a short unique identifier.
    /// </summary>
    /// <returns>A display name string.</returns>
    public string GetDisplayName()
    {
        string id = (internalId ??= Guid.NewGuid()).ToString().Substring(0, 4);

        return !string.IsNullOrEmpty(Name) ?
            $"{Name} ({GetType().Name}#{id})" :
            $"{GetType().Name}#{id}";
    }

    public override string ToString() => GetDisplayName();

    #endregion
}

/// <summary>
/// Represents the state of a drawable that requires re-computation.
/// </summary>
[Flags]
public enum InvalidationFlags
{
    /// <summary>
    /// The drawable is in a clean state and requires no updates.
    /// </summary>
    None = 0,

    /// <summary>
    /// The drawable's position, size, or other spatial properties have changed.
    /// This requires recalculating the model matrix and draw rectangle.
    /// </summary>
    DrawInfo = 1 << 0,

    /// <summary>
    /// The color of the drawable has changed.
    /// </summary>
    Colour = 1 << 1,

    /// <summary>
    /// A catch-all for all invalidation types.
    /// </summary>
    All = ~None
}
