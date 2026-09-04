// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// A floating, movable, resizable window for a debug tool. Hosted by <see cref="DebugWindowLayer"/>.
/// </summary>
public abstract partial class DebugWindow : Container
{
    public const float TITLE_HEIGHT = 26;

    private const float resize_grip_size = 16;
    private const float corner_radius = 6;

    /// <summary>
    /// Shown in the title bar. Include the shortcut that opens it.
    /// </summary>
    protected abstract string Title { get; }

    /// <summary>
    /// Size the window opens at the first time, before the user has moved or resized it.
    /// </summary>
    protected virtual Vector2 DefaultSize => new Vector2(760, 520);

    /// <summary>
    /// The smallest the user can drag the window. Also, the floor when the window is clamped into a
    /// parent smaller than itself.
    /// </summary>
    protected virtual Vector2 MinSize => new Vector2(320, 160);

    /// <summary>
    /// Title-bar tint, so several open windows are distinguishable at a glance. Conventionally, the
    /// color of the tool is already used for its heading.
    /// </summary>
    protected virtual Color Accent => Color.White;

    /// <summary>
    /// Whether closing this window should dispose it, so the next open rebuilds it from scratch.
    /// Defaults to false so a closed window is detached from the tree, which already means it costs
    /// nothing per frame, and keeping the instance preserves scroll position and selection.
    /// </summary>
    protected internal virtual bool DisposeOnClose => false;

    /// <summary>
    /// Raised when the user asks for this window to close. The layer owns what that does — the window
    /// never detaches itself.
    /// </summary>
    internal event Action<DebugWindow>? CloseRequested;

    /// <summary>
    /// Raised when the user interacts with the window's chrome, so the layer can bring it to the
    /// front. <see cref="DebugWindowLayer.NotifyMouseDown"/> covers clicks anywhere in the window;
    /// this covers the common gestures without needing that hook to be wired up.
    /// </summary>
    internal event Action<DebugWindow>? RaiseRequested;

    private readonly Container body;

    protected override Container Content => body;

    /// <summary>
    /// Parent size the window was last clamped against, so window resize re-clamps and a steady
    /// frame do nothing.
    /// </summary>
    private Vector2 lastClampedAgainst;

    protected DebugWindow()
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        Size = DefaultSize;

        Masking = true;
        CornerRadius = corner_radius;
        BorderThickness = 1;
        BorderColor = Color.FromArgb(40, 255, 255, 255);

        AddInternal(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.Black,
            Alpha = 0.92f
        });

        // Padding rather than an explicit height: the body then tracks the window as it is resized,
        // and Padding both shrinks the child coordinate space and offsets children into it.
        AddInternal(body = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding { Top = TITLE_HEIGHT },
            Masking = true
        });

        AddInternal(buildTitleBar());

        AddInternal(new DragHandle
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Size = new Vector2(resize_grip_size),
            Dragged = resizeBy,
            Pressed = requestRaise,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Color = Color.White,
                    Alpha = 0.15f
                }
            }
        });
    }

    private Drawable buildTitleBar()
    {
        var titleBar = new DragHandle
        {
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, TITLE_HEIGHT),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Dragged = moveBy,
            Pressed = requestRaise
        };

        titleBar.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Accent,
            Alpha = 0.18f
        });

        titleBar.Add(new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Position = new Vector2(8, 0),
            Text = Title,
            Font = FontUsage.Default.With(size: 15, weight: "Bold"),
            Color = Accent
        });

        titleBar.Add(new CloseButton
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            Position = new Vector2(-4, 0),
            Size = new Vector2(18),
            Action = () =>
            {
                requestRaise();
                CloseRequested?.Invoke(this);
            }
        });

        return titleBar;
    }

    /// <summary>
    /// Places the window, clamped into the parent the same way a drag would be.
    /// </summary>
    internal void SetBounds(Vector2 position, Vector2 size)
    {
        Size = new Vector2(Math.Max(MinSize.X, size.X), Math.Max(MinSize.Y, size.Y));
        Position = position;

        clampIntoParent();
    }

    internal Vector2 CurrentSize => Size;

    private void requestRaise() => RaiseRequested?.Invoke(this);

    private void moveBy(Vector2 delta)
    {
        Position += delta;
        clampIntoParent();
    }

    private void resizeBy(Vector2 delta)
    {
        Size = new Vector2(Size.X + delta.X, Size.Y + delta.Y);
        clampIntoParent();
    }

    /// <summary>
    /// Keeps the whole window inside its parent, shrinking it first if the parent is smaller than
    /// <see cref="MinSize"/> would allow. Dragging a window out of reach is the one way to lose it
    /// permanently, so this is not a nicety.
    /// </summary>
    private void clampIntoParent()
    {
        if (Parent is not { } parent)
            return;

        Vector2 available = parent.ChildSize;

        if (available.X <= 0 || available.Y <= 0)
            return;

        lastClampedAgainst = available;

        Size = new Vector2(
            Math.Clamp(Size.X, Math.Min(MinSize.X, available.X), available.X),
            Math.Clamp(Size.Y, Math.Min(MinSize.Y, available.Y), available.Y));

        Position = new Vector2(
            Math.Clamp(Position.X, 0, Math.Max(0, available.X - Size.X)),
            Math.Clamp(Position.Y, 0, Math.Max(0, available.Y - Size.Y)));
    }

    public override void Update()
    {
        base.Update();

        // The window is placed in pixels, so a shrinking client area can leave it partly or wholly
        // outside. Comparing against the last clamp keeps a steady frame free of work.
        if (Parent is { } parent && parent.ChildSize != lastClampedAgainst)
            clampIntoParent();
    }

    /// <summary>
    /// A window is a solid object, so a click that lands on it does not reach the app behind, and
    /// scrolling over it does not scroll the app behind. Neither is true outside its rectangle,
    /// which is the whole point of not being an <see cref="OverlayContainer"/>.
    /// </summary>
    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);
        requestRaise();
        return true;
    }

    public override bool OnScroll(ScrollEvent e) => true;

    public override bool OnHover(MouseEvent e) => true;

    /// <summary>
    /// A drag surface receptor
    /// </summary>
    private partial class DragHandle : Container
    {
        public Action<Vector2>? Dragged { get; init; }
        public Action? Pressed { get; init; }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            base.OnMouseDown(e);

            Pressed?.Invoke();
            return true;
        }

        public override bool OnDragStart(MouseButtonEvent e) => e.Button == MouseButton.Left;

        public override bool OnDrag(MouseEvent e)
        {
            Dragged?.Invoke(e.Delta);
            return true;
        }

        public override bool OnDragEnd(MouseButtonEvent e) => true;
    }

    private partial class CloseButton : ClickableContainer
    {
        private readonly Box background;

        public CloseButton()
        {
            Masking = true;
            CornerRadius = 3;

            Add(background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = Color.White,
                Alpha = 0.1f
            });

            Add(new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "X",
                Font = FontUsage.Default.With(size: 12, weight: "Bold"),
                Color = Color.White
            });
        }

        public override bool OnHover(MouseEvent e)
        {
            background.Color = Color.Red;
            background.Alpha = 0.6f;
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            background.Color = Color.White;
            background.Alpha = 0.1f;
            return base.OnHoverLost(e);
        }
    }
}
