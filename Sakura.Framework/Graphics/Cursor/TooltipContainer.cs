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

        var candidate = findTooltipTarget();

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
    /// Finds the deepest hovered drawable implementing <see cref="IHasTooltip"/> with non-empty text.
    /// When the container has user children (wrapping mode), searches only within them.
    /// When used as a full-screen overlay with no user children, searches the parent tree instead.
    /// </summary>
    private IHasTooltip? findTooltipTarget()
    {
        // If we have a content layer with children, search only within that subtree.
        if (contentLayer != null && contentLayer.Children.Count > 0)
            return searchDescendants(contentLayer);

        // Overlay mode: search siblings via our parent.
        if (Parent != null)
            return searchDescendants(Parent, skipDrawable: this);

        return null;
    }

    private IHasTooltip? searchDescendants(Container container, Drawable? skipDrawable = null)
    {
        // SortedChildren, not Children: "front-most first" is only true of insertion order when no
        // child has been given a Depth, and a tooltip on a raised control would otherwise lose to one
        // on a sibling that merely happens to have been added later. This mirrors what
        // InputManager.buildPositional walks.
        var sorted = container.SortedChildren;

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var child = sorted[i];

            // Skip ourselves and our internal tooltip.
            if (child == skipDrawable) continue;
            if (child is ITooltip) continue;

            if (!child.IsLoaded || !child.IsAlive || child.IsHidden)
                continue;

            if (!child.IsHovered)
                continue;

            // Recurse into containers — prefer the deepest match.
            if (child is Container childContainer)
            {
                var deep = searchDescendants(childContainer);
                if (deep != null) return deep;
            }

            if (child is IHasTooltip hasTooltip && !string.IsNullOrEmpty(hasTooltip.TooltipText))
                return hasTooltip;
        }

        return null;
    }
}
