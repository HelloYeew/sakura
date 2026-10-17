// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A container that arranges its children in a defined direction
/// with wrapping them to a new line when reach the boundary.
/// </summary>
/// <remarks>
/// <para>
/// The flow owns its children's positions (and resets <see cref="Drawable.RelativePositionAxes"/>), but
/// keeps their <see cref="Drawable.Anchor"/> and <see cref="Drawable.Origin"/>. Along the flow axis a
/// child takes its slot whatever its anchor (<see cref="Alignment"/> places the line); across it, the
/// child is aligned by its anchor: a <c>TopCentre</c> child in a vertical flow is centred, a
/// <c>CentreLeft</c> one in a horizontal flow is centred vertically. It aligns within its line, or within
/// the flow's whole cross size when there's a single line and that axis isn't auto-sized. Scale and
/// rotation turn about the child's own origin.
/// </para>
/// <para>
/// The arrangement (and auto-size) runs when the flow updates, and when its size is read while
/// auto-sizing, so it's current in the same frame.
/// </para>
/// </remarks>
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
            InvalidateChildLayout();
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
            InvalidateChildLayout();
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
            InvalidateChildLayout();
        }
    }

    /// <summary>
    /// A flow container's layout always depends on its children's geometry,
    /// regardless of whether it is auto-sizing.
    /// </summary>
    protected override bool LaysOutAroundChildren => true;

    public FlowContainer()
    {
        ChildLayoutTracksOwnSize = true;
    }

    /// <summary>The space the children were last arranged in.</summary>
    private Vector2 arrangedIn = new Vector2(float.NaN);

    /// <summary>
    /// The flow's own size decides where lines wrap and what a single line aligns within (and anchors
    /// place against it), so a change to it rearranges the children. Its position doesn't.
    /// </summary>
    protected override bool IsChildLayoutStale() => ResolveDrawSize(base.Size) - Padding.Total != arrangedIn;

    protected override void PerformChildLayout() => PerformLayout();

    /// <summary>A run of children laid out along the flow axis together.</summary>
    private struct Line
    {
        public int Start;
        public int End;
        public float FlowStart;
        public float CrossStart;
        public float CrossExtent;
    }

    /// <summary>
    /// The children taking part in the layout, and its lines. Reused between layout passes so filtering
    /// out hidden children costs no allocation per frame.
    /// </summary>
    private readonly List<Drawable> presentChildren = new List<Drawable>();

    /// <summary>Each present child's drawn size for this pass, measured once (see <see cref="getChildDrawSize"/>).</summary>
    private readonly List<Vector2> childSizes = new List<Vector2>();

    private readonly List<Line> lines = new List<Line>();

    /// <summary>
    /// Arranges the children and auto-sizes around them. Call <see cref="Container.EnsureChildLayout"/>
    /// rather than this: it runs this only when something changed.
    /// </summary>
    protected virtual void PerformLayout()
    {
        presentChildren.Clear();
        childSizes.Clear();
        lines.Clear();

        foreach (var child in Children)
        {
            if (child.AlwaysPresent || child.Alpha > 0)
            {
                presentChildren.Add(child);
                childSizes.Add(getChildDrawSize(child));
            }
        }

        var children = presentChildren;
        int count = children.Count;
        if (count == 0)
        {
            applyAutoSize(0, 0);
            arrangedIn = ChildSize;
            return;
        }

        bool horizontal = Direction == FlowDirection.Horizontal;
        bool autoX = (AutoSizeAxes & Axes.X) != 0;
        bool autoY = (AutoSizeAxes & Axes.Y) != 0;
        bool autoFlow = horizontal ? autoX : autoY;
        bool autoCross = horizontal ? autoY : autoX;

        // The space before auto-size: ChildSize, which respects padding.
        var available = ChildSize;

        // limit along the flow axis (no limit when that axis auto-sizes).
        float flowLimit = autoFlow ? float.MaxValue : (horizontal ? available.X : available.Y);

        float flowSpacing = horizontal ? Spacing.X : Spacing.Y;
        float crossSpacing = horizontal ? Spacing.Y : Spacing.X;

        // Content bounds the auto-size fits, leaving out children that bypass it.
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
                var size = childSizes[lineEnd] + children[lineEnd].Margin.Total;
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
            float flowStart = Alignment switch
            {
                FlowAlignment.Center => free / 2f,
                FlowAlignment.End => free,
                _ => 0f,
            };

            lines.Add(new Line { Start = i, End = lineEnd, FlowStart = flowStart, CrossStart = lineStartCross, CrossExtent = lineCrossExtent });

            // measure pass over the same range, for auto-size
            float currentFlow = flowStart;

            for (int j = i; j < lineEnd; j++)
            {
                var child = children[j];
                var totalSize = childSizes[j] + child.Margin.Total;
                Vector2 slot = horizontal ? new Vector2(currentFlow, lineStartCross) : new Vector2(lineStartCross, currentFlow);

                if ((child.BypassAutoSizeAxes & Axes.X) == 0 && slot.X + totalSize.X > maxRight) maxRight = slot.X + totalSize.X;
                if ((child.BypassAutoSizeAxes & Axes.Y) == 0 && slot.Y + totalSize.Y > maxBottom) maxBottom = slot.Y + totalSize.Y;

                currentFlow += (horizontal ? totalSize.X : totalSize.Y) + flowSpacing;
            }

            // advance to the next line along the cross axis.
            lineStartCross += lineCrossExtent + crossSpacing;
            i = lineEnd;
        }

        applyAutoSize(maxRight, maxBottom);

        // Children are placed against the flow's final content size: their anchors are relative to it.
        // (A read of our size here returns the size just applied rather than re-entering the layout.)
        Vector2 content = ChildSize;
        arrangedIn = content;
        float contentCross = horizontal ? content.Y : content.X;

        foreach (var line in lines)
        {
            // One line in a flow that doesn't auto-size across aligns within the whole cross size.
            float crossRoom = lines.Count == 1 && !autoCross ? contentCross : line.CrossExtent;
            float currentFlow = line.FlowStart;

            for (int j = line.Start; j < line.End; j++)
            {
                var child = children[j];
                var drawSize = childSizes[j];
                var totalSize = drawSize + child.Margin.Total;

                // The flow owns positions.
                if (child.RelativePositionAxes != Axes.None)
                    child.RelativePositionAxes = Axes.None;

                Vector2 position;

                if (child.Anchor == Anchor.TopLeft && child.Origin == Anchor.TopLeft)
                {
                    // The common case: the slot's corner is the position.
                    position = horizontal ? new Vector2(currentFlow, line.CrossStart) : new Vector2(line.CrossStart, currentFlow);
                }
                else
                {
                    Vector2 anchor = GetAnchorOriginVector(child.Anchor);
                    float crossSize = horizontal ? totalSize.Y : totalSize.X;
                    float cross = line.CrossStart + (crossRoom - crossSize) * (horizontal ? anchor.Y : anchor.X);

                    // Where the child's margin box goes, in content space.
                    Vector2 box = horizontal ? new Vector2(currentFlow, cross) : new Vector2(cross, currentFlow);
                    position = positionFor(child, anchor, box, drawSize, content);
                }

                if (!Precision.AlmostEquals(child.Position, position))
                    child.Position = position;

                // advance along the flow axis.
                currentFlow += (horizontal ? totalSize.X : totalSize.Y) + flowSpacing;
            }
        }
    }

    /// <summary>
    /// The <see cref="Drawable.Position"/> that puts <paramref name="child"/>'s margin box's top-left at
    /// <paramref name="box"/>, given its anchor and origin, in a content area of <paramref name="content"/>.
    /// The inverse of how a drawable is placed: anchor point, plus position, less origin.
    /// </summary>
    private static Vector2 positionFor(Drawable child, Vector2 anchor, Vector2 box, Vector2 drawSize, Vector2 content)
    {
        Vector2 origin = GetAnchorOriginVector(child.Origin);
        Vector2 margin = child.Margin.Total;

        return new Vector2(
            box.X - anchor.X * (content.X - margin.X) + origin.X * drawSize.X,
            box.Y - anchor.Y * (content.Y - margin.Y) + origin.Y * drawSize.Y);
    }

    /// <summary>
    /// Applies the auto-sized dimensions from the computed content bounds.
    /// </summary>
    private void applyAutoSize(float maxRight, float maxBottom)
    {
        Vector2 newSize = base.Size;

        // maxRight/maxBottom are padding-free (see PerformLayout), so add the full padding on each
        // axis to size the container around its content plus both padding edges.
        if ((AutoSizeAxes & Axes.X) != 0 && (RelativeSizeAxes & Axes.X) == 0)
            newSize.X = ClampAutoSize(maxRight + Padding.Total.X, Axes.X);

        if ((AutoSizeAxes & Axes.Y) != 0 && (RelativeSizeAxes & Axes.Y) == 0)
            newSize.Y = ClampAutoSize(maxBottom + Padding.Total.Y, Axes.Y);

        if (!Precision.AlmostEquals(base.Size, newSize))
            ApplyAutoSizeTarget(newSize);
    }

    /// <summary>
    /// A child's size as it will be drawn (its <see cref="Drawable.DrawSize"/>, margins already taken off a
    /// relative axis), except that a child sized relative to the flow takes no room on an axis the flow
    /// auto-sizes, which would otherwise feed back into itself.
    /// </summary>
    private Vector2 getChildDrawSize(Drawable child)
    {
        Vector2 size = child.DrawSize;

        if ((child.RelativeSizeAxes & Axes.X) != 0 && (AutoSizeAxes & Axes.X) != 0)
            size.X = 0;
        if ((child.RelativeSizeAxes & Axes.Y) != 0 && (AutoSizeAxes & Axes.Y) != 0)
            size.Y = 0;

        return size;
    }
}
