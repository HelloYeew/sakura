// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A lowest level of the component hierarchy. All drawable components should be inherited from this class.
/// </summary>
public class Drawable
{
    public Container? Parent { get; internal set; }

    public bool IsHovered { get; private set; }
    public bool IsDragged { get; private set; }
    internal bool IsLoaded { get; private set; }

    private Anchor anchor = Anchor.Centre;
    private Anchor origin = Anchor.TopLeft;
    private Vector2 position = Vector2.Zero;
    private Vector2 size = Vector2.Zero;
    private Vector2 scale = Vector2.One;
    private float rotation;
    private Axes relativeSizeAxes = Axes.None;
    private Axes relativePositionAxes = Axes.None;
    private Color color = Color.White;
    private float alpha = 1f;
    private MarginPadding margin;
    private MarginPadding padding;
    private float depth;
    private bool alwaysPresent;

    /// <summary>
    /// A clock for this drawable, time is relative to the parent's clock
    /// </summary>
    public IClock Clock { get; internal set; }

    /// <summary>
    /// The scheduler for this drawable, used for delaying and scheduling tasks.
    /// </summary>
    public Scheduler Scheduler { get; }

    private readonly List<ITransform> transforms = new();

    /// <summary>
    /// An internal property used by the transform extension methods to sequence animations.
    /// It represents the delay before the next transformation can begin.
    /// </summary>
    internal double TimeUntilTransformsCanStart { get; set; }

    public float DrawAlpha { get; private set; } = 1f;

    /// <summary>
    /// An invalidation flag representing which aspects of the drawable need to be recomputed.
    /// </summary>
    protected InvalidationFlags Invalidation = InvalidationFlags.All;

    protected readonly Vertex[] Vertices = new Vertex[6];

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

    public Vector2 Size
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
            alpha = Math.Clamp(value, 0f, 1f);
            Invalidate(InvalidationFlags.Colour);
        }
    }

    public float Depth
    {
        get => depth;
        set
        {
            if (Math.Abs(depth - value) < 0.0001f) return;
            depth = value;
            // Re-sort children in parent required
            Parent?.Invalidate(InvalidationFlags.DrawInfo);
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

    public void Hide() => Alpha = 0f;
    public void Show() => Alpha = 1f;
    public bool IsHidden => Alpha <= 0f;

    public Texture? Texture { get; set; }

    // Caches for computed values
    public RectangleF DrawRectangle { get; private set; }
    public Vector2 DrawSize { get; private set; }
    public Matrix4x4 ModelMatrix = Matrix4x4.Identity;

    #region Calculation of Draw Info

    protected virtual void UpdateTransforms()
    {
        DrawAlpha = (Parent?.DrawAlpha ?? 1f) * Alpha;

        Matrix4x4 localMatrix;
        Vector2 finalDrawSize;
        Vector2 originVector = GetAnchorOriginVector(Origin);

        if (Parent == null)
        {
            // This is the root drawable.
            finalDrawSize = Size;

            var finalScale = new Vector3(finalDrawSize.X * Scale.X, finalDrawSize.Y * Scale.Y, 1);
            var finalPosition = new Vector3(Position.X, Position.Y, 0);

            // Transform order: Origin Translation -> Scale -> Rotation -> Position Translation
            var m = Matrix4x4.CreateTranslation(-originVector.X, -originVector.Y, 0); // Translate so origin is at (0,0)
            m *= Matrix4x4.CreateScale(finalScale); // Scale around (0,0)
            m *= Matrix4x4.CreateRotationZ((float)(Rotation * Math.PI / 180.0f)); // Rotate around (0,0)
            m *= Matrix4x4.CreateTranslation(finalPosition); // Translate to final position

            ModelMatrix = m;

            DrawSize = finalDrawSize;
        }
        else
        {
            // This is a child drawable

            // Final pixel size of the parent's content area.
            Vector2 parentPixelSize = Parent.ChildSize;

            // prevent division by zero for drawables with no area.
            if (parentPixelSize.X == 0) parentPixelSize.X = 1;
            if (parentPixelSize.Y == 0) parentPixelSize.Y = 1;

            // Calculate scale relative to parent
            Vector2 localScale = Size;
            if ((RelativeSizeAxes & Axes.X) == 0)
                localScale.X /= parentPixelSize.X;
            if ((RelativeSizeAxes & Axes.Y) == 0)
                localScale.Y /= parentPixelSize.Y;

            // Calculate position relative to parent
            Vector2 localPosition = Position;
            if ((RelativePositionAxes & Axes.X) == 0)
                localPosition.X /= parentPixelSize.X;
            if ((RelativePositionAxes & Axes.Y) == 0)
                localPosition.Y /= parentPixelSize.Y;

            MarginPadding localMargin = Margin;
            localMargin.Left /= parentPixelSize.X;
            localMargin.Right /= parentPixelSize.X;
            localMargin.Top /= parentPixelSize.Y;
            localMargin.Bottom /= parentPixelSize.Y;

            // Anchor and Origin are naturally relative, so they work correctly in this 0-1 space.
            Vector2 anchorPosition = GetAnchorOriginVector(Anchor);
            Vector2 finalOriginPosition = anchorPosition + localPosition + new Vector2(localMargin.Left - localMargin.Right, localMargin.Top - localMargin.Bottom);

            var finalScale = new Vector3(localScale.X * Scale.X, localScale.Y * Scale.Y, 1);
            var finalPosition = new Vector3(finalOriginPosition.X, finalOriginPosition.Y, 0);

            var m = Matrix4x4.CreateTranslation(-originVector.X, -originVector.Y, 0);
            m *= Matrix4x4.CreateScale(finalScale);
            m *= Matrix4x4.CreateRotationZ((float)(Rotation * Math.PI / 180.0f));
            m *= Matrix4x4.CreateTranslation(finalPosition);

            localMatrix = m;
            ModelMatrix = localMatrix * Parent.ModelMatrix;

            finalDrawSize = Size;
            if ((RelativeSizeAxes & Axes.X) != 0) finalDrawSize.X *= parentPixelSize.X;
            if ((RelativeSizeAxes & Axes.Y) != 0) finalDrawSize.Y *= parentPixelSize.Y;
        }

        DrawSize = finalDrawSize;

        GenerateVertices();
    }

    protected virtual void GenerateVertices()
    {
        var calculatedColor = new System.Numerics.Vector4(Color.R / 255f, Color.G / 255f, Color.B / 255f, DrawAlpha);

        var vTopLeft = Vector4.Transform(new Vector4(0, 0, 0, 1), ModelMatrix);
        var vTopRight = Vector4.Transform(new Vector4(1, 0, 0, 1), ModelMatrix);
        var vBottomLeft = Vector4.Transform(new Vector4(0, 1, 0, 1), ModelMatrix);
        var vBottomRight = Vector4.Transform(new Vector4(1, 1, 0, 1), ModelMatrix);

        var topLeft = new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = new Vector2(0, 0), Color = calculatedColor };
        var topRight = new Vertex { Position = new Vector2(vTopRight.X, vTopRight.Y), TexCoords = new Vector2(1, 0), Color = calculatedColor };
        var bottomLeft = new Vertex { Position = new Vector2(vBottomLeft.X, vBottomLeft.Y), TexCoords = new Vector2(0, 1), Color = calculatedColor };
        var bottomRight = new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = new Vector2(1, 1), Color = calculatedColor };

        // Triangle 1
        Vertices[0] = topLeft;
        Vertices[1] = topRight;
        Vertices[2] = bottomRight;

        // Triangle 2
        Vertices[3] = bottomRight;
        Vertices[4] = bottomLeft;
        Vertices[5] = topLeft;

        // Calculate DrawRectangle in screen space
        float minX = Math.Min(vTopLeft.X, Math.Min(vTopRight.X, Math.Min(vBottomLeft.X, vBottomRight.X)));
        float minY = Math.Min(vTopLeft.Y, Math.Min(vTopRight.Y, Math.Min(vBottomLeft.Y, vBottomRight.Y)));
        float maxX = Math.Max(vTopLeft.X, Math.Max(vTopRight.X, Math.Max(vBottomLeft.X, vBottomRight.X)));
        float maxY = Math.Max(vTopLeft.Y, Math.Max(vTopRight.Y, Math.Max(vBottomLeft.Y, vBottomRight.Y)));

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    public bool Contains(Vector2 screenSpacePos) => DrawRectangle.Contains(screenSpacePos);

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

    /// <summary>
    /// Called to perform loading tasks to load required resources and dependencies.
    /// Called once before the first update.
    /// This method is recursively called down the drawable hierarchy.
    /// </summary>
    public virtual void Load()
    {
        if (IsLoaded) return;

        loadDependencies();

        IsLoaded = true;
    }

    /// <summary>
    /// Called after the drawable and all its children have been loaded.
    /// This method is recursively called down the drawable hierarchy.
    /// </summary>
    public virtual void LoadComplete()
    {
    }

    public virtual void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0)
            return;

        renderer.DrawVertices(Vertices, Texture ?? Texture.WhitePixel);
    }

    /// <summary>
    /// Marks all or part of this drawable as requiring re-computation.
    /// </summary>
    /// <param name="flags">An <see cref="InvalidationFlags"/> flag representing which aspects of the drawable need to be recomputed.</param>
    /// <param name="propagateToParent">Whether this invalidation should also invalidate this drawable's parent.</param>
    public virtual void Invalidate(InvalidationFlags flags = InvalidationFlags.All, bool propagateToParent = true)
    {
        if ((Invalidation & flags) == flags)
            return; // Already invalidated for these flags.

        Invalidation |= flags;

        if (propagateToParent && (flags & InvalidationFlags.DrawInfo) != 0)
            Parent?.Invalidate(InvalidationFlags.DrawInfo);
    }

    #region Dependency Injection

    private IReadOnlyDependencyContainer dependencies = null!;
    private DependencyContainer? ownDependencies;

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
        if (ownDependencies == null)
            throw new InvalidOperationException($"Cannot cache dependencies before {nameof(Load)} has been called.");

        ownDependencies.Cache(instance);
    }

    private void loadDependencies()
    {
        IReadOnlyDependencyContainer? parentContainer = getParentDependencyContainer();
        dependencies = ownDependencies = new DependencyContainer(parentContainer);

        // inject dependencies into [Resolved] fields/properties
        dependencies.Inject(this);

        var loadMethod = GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.GetCustomAttribute<BackgroundDependencyLoaderAttribute>() != null);

        if (loadMethod != null)
        {
            // Check if the method has parameters we need to inject
            var methodParams = loadMethod.GetParameters();

            if (methodParams.Length > 0)
            {
                // resolve from main container
                object?[] resolvedParams = new object?[methodParams.Length];

                // get the generic Get<T> method from the dependency container
                var getMethod = dependencies.GetType().GetMethod(nameof(IReadOnlyDependencyContainer.Get), BindingFlags.Public | BindingFlags.Instance);
                if (getMethod == null) throw new InvalidOperationException("Could not find Get method on dependency container.");

                for (int i = 0; i < methodParams.Length; i++)
                {
                    // foreach parameter, resolve the dependency using reflection
                    var paramType = methodParams[i].ParameterType;
                    var concreteGetMethod = getMethod.MakeGenericMethod(paramType);
                    resolvedParams[i] = concreteGetMethod.Invoke(dependencies, null);
                }

                // invoke the method with the resolved dependencies
                loadMethod.Invoke(this, resolvedParams);
            }
            else
            {
                // no parameters in load method, invoke as before
                loadMethod.Invoke(this, null);
            }
        }
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

    public Drawable()
    {
        Texture = Texture.WhitePixel;
        Clock = new Clock(true);
        Scheduler = new Scheduler(Clock);
    }

    public virtual void Update()
    {
        if (!IsLoaded) return;

        (Clock as FramedClock)?.Update();
        Scheduler.Update();
        applyTransforms();

        if (Invalidation == InvalidationFlags.None)
            return;

        if ((Invalidation & (InvalidationFlags.DrawInfo | InvalidationFlags.Colour)) != 0)
        {
            UpdateTransforms();
        }

        Invalidation = InvalidationFlags.None;
    }

    #region Transformation Management

    private void applyTransforms()
    {
        if (transforms.Count == 0)
            return;

        for (int i = transforms.Count - 1; i >= 0; i--)
        {
            var t = transforms[i];

            // Apply the transform if its start time has been reached.
            if (Clock.CurrentTime >= t.StartTime)
            {
                t.Apply(this, Clock.CurrentTime);
            }

            // Remove the transform if it has completed.
            if (Clock.CurrentTime >= t.EndTime)
            {
                // Ensure the final value is applied exactly.
                t.Apply(this, t.EndTime);
                transforms.RemoveAt(i);
            }
        }
    }

    internal void AddTransform(Transform transform)
    {
        transforms.Add(transform);
        // We don't sort here for performance; looping backwards in ApplyTransforms handles completed transforms.
    }

    /// <summary>
    /// Gets the end time of the latest-finishing transformation on this drawable.
    /// </summary>
    public double GetLatestTransformEndTime() => transforms.Count > 0 ? transforms.Max(t => t.EndTime) : Clock.CurrentTime;

    /// <summary>
    /// Removes all transformations from this drawable.
    /// </summary>
    /// <param name="propagateToChildren">Whether to also clear transformations on all children.</param>
    public void ClearTransforms(bool propagateToChildren = false)
    {
        transforms.Clear();
        TimeUntilTransformsCanStart = 0;

        if (propagateToChildren && this is Container c)
        {
            foreach (var child in c.Children)
                child.ClearTransforms(true);
        }
    }

    internal void LoopLatestTransforms()
    {
        if (!transforms.Any())
            return;

        double latestEndTime = GetLatestTransformEndTime();

        foreach (var t in transforms)
        {
            if (t.EndTime == latestEndTime)
                t.IsLooping = true;
        }
    }

    /// <summary>
    /// Instantly finishes all transformations, applying their final values.
    /// </summary>
    /// <param name="propagateToChildren">Whether to also finish transformations on all children.</param>
    public void FinishTransforms(bool propagateToChildren = false)
    {
        foreach (var t in transforms)
            t.Apply(this, t.EndTime);

        ClearTransforms(propagateToChildren);
    }

    #endregion

    #region Event Handlers

    public virtual bool OnMouseDown(MouseButtonEvent e)
    {
        if (e.Clicks >= 3)
            return OnTripleClick(e);
        if (e.Clicks == 2)
            return OnDoubleClick(e);
        if (e.Clicks == 1)
            OnClick(e);

        // Potential start of a drag operation.
        if (e.Button == MouseButton.Left)
        {
            IsDragged = true;
            OnDragStart(e);
        }

        return false;
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
