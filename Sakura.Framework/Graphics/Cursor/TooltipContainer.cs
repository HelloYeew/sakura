// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// A <see cref="Container"/> that shows tooltips for any child implementing <see cref="IHasTooltip"/>.
/// The tooltip only appears after the cursor has stayed within <see cref="AppearRadius"/> pixels
/// for at least <see cref="AppearDelay"/> milliseconds.
/// </summary>
public partial class TooltipContainer : Container, IRemoveFromDrawVisualiser
{
    /// <summary>
    /// Milliseconds the cursor must remain still before the tooltip appears.
    /// </summary>
    protected virtual double AppearDelay => 220;

    /// <summary>
    /// Pixel radius the cursor must stay within during <see cref="AppearDelay"/>.
    /// </summary>
    protected virtual float AppearRadius => 20;

    /// <summary>
    /// Pixel offset applied to the tooltip position from the cursor.
    /// </summary>
    protected virtual Vector2 CursorOffset => new Vector2(14, 14);

    private readonly ITooltip tooltip;
    private IHasTooltip? currentTarget;
    private IHasTooltip? lastCandidate;

    private Vector2 lastMousePosition;
    private bool tooltipShown;

    private Vector2 stableAnchor;
    private double stableSince;

    public TooltipContainer()
    {
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);

        // The tooltip drawable lives as a direct internal child, so it floats
        // above all content children but is not treated as user content.
        var concreteTooltip = CreateTooltip();
        tooltip = concreteTooltip;
        AddInternal(concreteTooltip);
    }

    /// <summary>
    /// Override to create a custom tooltip drawable.
    /// </summary>
    protected virtual BasicTooltip CreateTooltip() => new BasicTooltip();

    private Container? contentLayer;

    protected override Container Content => contentLayer ??= createContentLayer();

    private Container createContentLayer()
    {
        var layer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
        };

        // Ordering is by Depth, not insertion: the tooltip carries float.MaxValue so it draws over
        // this layer regardless of which was added first.
        AddInternal(layer);
        return layer;
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        lastMousePosition = ToLocalSpace(e.ScreenSpaceMousePosition);
        return base.OnMouseMove(e);
    }

    public override void Update()
    {
        base.Update();

        var manager = GetContainingInputManager();
        if (manager != null)
            lastMousePosition = ToLocalSpace(manager.CurrentState.MousePosition);

        double now = Clock.CurrentTime;

        // Restart the dwell whenever the cursor leaves the radius it settled in.
        float dx = lastMousePosition.X - stableAnchor.X;
        float dy = lastMousePosition.Y - stableAnchor.Y;

        if (dx * dx + dy * dy > AppearRadius * AppearRadius)
        {
            stableAnchor = lastMousePosition;
            stableSince = now;
        }

        // A finger leaves no pointer resting anywhere, so nothing dwells long enough to want a tooltip.
        var candidate = manager?.CurrentState.LastPointerWasTouch == true ? null : findTooltipTarget(manager);

        if (candidate != lastCandidate)
        {
            // A new target starts its own dwell, so moving between two adjacent targets does not
            // inherit the time already spent on the first one.
            stableAnchor = lastMousePosition;
            stableSince = now;
            lastCandidate = candidate;
        }

        if (candidate == null)
        {
            hideTooltip();
            return;
        }

        // Check whether the cursor has been stable long enough.
        if (!isCursorStable())
        {
            // Cursor is moving — hide if we haven't shown yet; keep shown if already displayed.
            if (!tooltipShown)
                return;
        }
        else if (!tooltipShown)
        {
            showTooltip(candidate);
        }

        if (tooltipShown)
        {
            // Keep content fresh and reposition.
            string? content = candidate.TooltipText;
            if (string.IsNullOrEmpty(content))
            {
                hideTooltip();
                return;
            }

            tooltip.SetContent(content);
            tooltip.Move(computeTooltipPosition());
        }
    }

    /// <summary>
    /// Whether the cursor has dwelt within <see cref="AppearRadius"/> for <see cref="AppearDelay"/>.
    /// </summary>
    private bool isCursorStable() => Clock.CurrentTime - stableSince >= AppearDelay;

    private void showTooltip(IHasTooltip target)
    {
        tooltipShown = true;
        currentTarget = target;
        tooltip.SetContent(target.TooltipText ?? "");
        tooltip.Move(computeTooltipPosition());
        tooltip.Show();
    }

    private void hideTooltip()
    {
        if (!tooltipShown && currentTarget == null)
            return;

        tooltipShown = false;
        currentTarget = null;
        tooltip.Hide();
    }

    private Vector2 computeTooltipPosition()
    {
        var pos = lastMousePosition + CursorOffset;

        // Clamp so tooltip doesn't escape the container.
        if (tooltip is Drawable tooltipDrawable)
        {
            float w = tooltipDrawable.DrawSize.X;
            float h = tooltipDrawable.DrawSize.Y;

            if (pos.X + w > DrawSize.X - 5)
                pos.X = lastMousePosition.X - w - 4;

            if (pos.Y + h > DrawSize.Y - 5)
                pos.Y = lastMousePosition.Y - h - 4;
        }

        return pos;
    }

    /// <summary>
    /// Finds the front-most hovered drawable implementing <see cref="IHasTooltip"/> with non-empty text.
    /// When the container has user children (wrapping mode), searches only within them.
    /// When used as a full-screen overlay with no user children, searches the parent tree instead.
    /// </summary>
    /// <remarks>
    /// Read from the input manager's positional queue, which lists what is under the cursor nearest first, rather than
    /// by walking down through hovered children. A control that blocks hover (a button) leaves its own parents
    /// un-hovered, so a walk from the top stopped at the first of them and never reached the control.
    /// </remarks>
    private IHasTooltip? findTooltipTarget(InputManager? manager)
    {
        if (manager == null)
            return null;

        var scope = contentLayer != null && contentLayer.Children.Count > 0 ? contentLayer : Parent;

        if (scope == null)
            return null;

        var queue = manager.PositionalInputQueue;

        for (int i = 0; i < queue.Count; i++)
        {
            var drawable = queue[i];

            // Not hovered means it is behind something that blocks hover, such as a shown overlay.
            if (!drawable.IsHovered || drawable is not IHasTooltip hasTooltip || string.IsNullOrEmpty(hasTooltip.TooltipText))
                continue;

            if (!drawable.IsLoaded || !drawable.IsAlive || drawable.IsEffectivelyHidden)
                continue;

            if (isWithinScope(drawable, scope))
                return hasTooltip;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="drawable"/> sits below <paramref name="scope"/>, and not inside this container's own
    /// internals.
    /// </summary>
    private bool isWithinScope(Drawable drawable, Drawable scope)
    {
        for (var current = drawable.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, scope))
                return true;

            // Inside this container without passing through the scope first: part of the tooltip itself.
            if (ReferenceEquals(current, this))
                return false;
        }

        return false;
    }
}
