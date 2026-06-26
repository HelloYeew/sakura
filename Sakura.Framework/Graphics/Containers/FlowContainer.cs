// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A container that arranges its children in a defined direction
/// with wrapping them to a new line when reach the boundary.
/// </summary>
public partial class FlowContainer : Container
{
    private FlowDirection direction = FlowDirection.Horizontal;
    private Vector2 spacing = Vector2.Zero;
    private FlowAlignment alignment = FlowAlignment.Start;

    /// <summary>
    /// The direction of flow layout on how children arranged.
    /// </summary>
    public FlowDirection Direction
    {
        get => direction;
        set
        {
            if (direction == value)
                return;

            direction = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The spacing between children in pixels.
    /// </summary>
    public Vector2 Spacing
    {
        get => spacing;
        set
        {
            if (spacing == value)
                return;

            spacing = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// How children are aligned along the flow axis within each line.
    /// Defaults to <see cref="FlowAlignment.Start"/>. Has no effect on an
    /// auto-sizing axis, since the container then fits its content exactly.
    /// </summary>
    public FlowAlignment Alignment
    {
        get => alignment;
        set
        {
            if (alignment == value)
                return;

            alignment = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// A flow container's layout always depends on its children's geometry,
    /// regardless of whether it is auto-sizing.
    /// </summary>
    protected internal override void OnChildGeometryInvalidated() => InvalidateLayout();

    public override void Update()
    {
        bool layoutWasInvalidated = (Invalidation & InvalidationFlags.DrawInfo) != 0;

        if (layoutWasInvalidated)
        {
            UpdateTransforms();
            PerformLayout();
        }

        base.Update();
    }

    protected override void UpdateAutoSize()
    {
        // Do nothing since PerformLayout will handle auto-sizing based on content size.
    }

    /// <summary>
    /// Calculates and applies the position for each child.
    /// </summary>
    protected virtual void PerformLayout()
    {
        var children = Children;
        int count = children.Count;
        if (count == 0)
        {
            applyAutoSize(0, 0);
            return;
        }

        bool horizontal = Direction == FlowDirection.Horizontal;
        bool autoX = (AutoSizeAxes & Axes.X) != 0;
        bool autoY = (AutoSizeAxes & Axes.Y) != 0;

        // use ChildSize, which is correct and respects Padding.
        var maxBounds = ChildSize;

        // limit along the flow axis (no limit when that axis auto-sizes).
        bool autoFlow = horizontal ? autoX : autoY;
        float flowLimit = autoFlow ? float.MaxValue : (horizontal ? maxBounds.X : maxBounds.Y);

        float flowSpacing = horizontal ? Spacing.X : Spacing.Y;
        float crossSpacing = horizontal ? Spacing.Y : Spacing.X;

        float maxRight = 0;
        float maxBottom = 0;
        float lineStartCross = 0; // cross-axis position of the current line

        int i = 0;
        while (i < count)
        {
            // find the [i, lineEnd) range that fits on this line
            int lineEnd = i;
            float lineFlowExtent = 0; // sum of item extents + interior gaps
            float lineCrossExtent = 0; // tallest/widest item on the cross axis

            while (lineEnd < count)
            {
                var size = getChildTotalSize(children[lineEnd]);
                float flow = horizontal ? size.X : size.Y;
                float cross = horizontal ? size.Y : size.X;

                // would adding this child overflow the line? (never break a line of one)
                float tentative = lineEnd == i ? flow : lineFlowExtent + flowSpacing + flow;
                if (lineEnd > i && tentative > flowLimit)
                    break;

                lineFlowExtent = tentative;
                if (cross > lineCrossExtent) lineCrossExtent = cross;
                lineEnd++;
            }

            // alignment: distribute free space along the flow axis
            float free = autoFlow || flowLimit == float.MaxValue ? 0 : Math.Max(0, flowLimit - lineFlowExtent);
            float currentFlow = Alignment switch
            {
                FlowAlignment.Center => free / 2f,
                FlowAlignment.End => free,
                _ => 0f,
            };

            // position pass over the same range
            for (int j = i; j < lineEnd; j++)
            {
                var child = children[j];
                var drawSize = getChildDrawSize(child);
                var totalSize = drawSize + child.Margin.Total;

                // must control these properties for layout to work.
                if (child.RelativePositionAxes != Axes.None)
                    child.RelativePositionAxes = Axes.None; // reset to absolute positioning
                if (child.Anchor != Anchor.TopLeft)
                    child.Anchor = Anchor.TopLeft; // Reset anchor to top-left
                if (child.Origin != Anchor.TopLeft)
                    child.Origin = Anchor.TopLeft; // Reset origin to top-left

                // resolve flow/cross coordinates back into x/y.
                // for horizontal flow, X is the flow axis and Y is the cross axis.
                // for vertical flow, Y is the flow axis and X is the cross axis.
                Vector2 basePos = horizontal
                    ? new Vector2(currentFlow, lineStartCross)
                    : new Vector2(lineStartCross, currentFlow);

                // include the child's own margin and the container's padding.
                var childPosPixels = new Vector2(basePos.X + child.Margin.Left, basePos.Y + child.Margin.Top);
                var finalPos = new Vector2(childPosPixels.X + Padding.Left, childPosPixels.Y + Padding.Top);

                if (!Precision.AlmostEquals(child.Position, finalPos))
                    child.Position = finalPos;

                // track content size (for auto-sizing).
                float childRight = childPosPixels.X + drawSize.X + child.Margin.Right;
                float childBottom = childPosPixels.Y + drawSize.Y + child.Margin.Bottom;

                if (childRight > maxRight) maxRight = childRight;
                if (childBottom > maxBottom) maxBottom = childBottom;

                // advance along the flow axis.
                currentFlow += (horizontal ? totalSize.X : totalSize.Y) + flowSpacing;
            }

            // advance to the next line along the cross axis.
            lineStartCross += lineCrossExtent + crossSpacing;
            i = lineEnd;
        }

        applyAutoSize(maxRight, maxBottom);
    }

    /// <summary>
    /// Total size of a child along both axes (draw size plus margin), used by the measure pass.
    /// </summary>
    private Vector2 getChildTotalSize(Drawable child) => getChildDrawSize(child) + child.Margin.Total;

    /// <summary>
    /// Applies the auto-sized dimensions from the computed content bounds.
    /// </summary>
    private void applyAutoSize(float maxRight, float maxBottom)
    {
        Vector2 newSize = Size;

        if ((AutoSizeAxes & Axes.X) != 0)
            newSize.X = maxRight + Padding.Right;

        if ((AutoSizeAxes & Axes.Y) != 0)
            newSize.Y = maxBottom + Padding.Bottom;

        if (!Precision.AlmostEquals(Size, newSize))
            Size = newSize;
    }

    /// <summary>
    /// Calculates a child's pixel size based on its properties
    /// and this container's available space.
    /// </summary>
    private Vector2 getChildDrawSize(Drawable child)
    {
        // use ChildSize, which is now correct.
        Vector2 parentPixelSize = ChildSize;
        Vector2 finalDrawSize = child.Size;

        if (parentPixelSize.X <= 0) parentPixelSize.X = 1;
        if (parentPixelSize.Y <= 0) parentPixelSize.Y = 1;

        if ((child.RelativeSizeAxes & Axes.X) != 0)
            finalDrawSize.X = (AutoSizeAxes & Axes.X) != 0 ? 0 : finalDrawSize.X * parentPixelSize.X;
        if ((child.RelativeSizeAxes & Axes.Y) != 0)
            finalDrawSize.Y = (AutoSizeAxes & Axes.Y) != 0 ? 0 : finalDrawSize.Y * parentPixelSize.Y;

        return finalDrawSize;
    }
}
