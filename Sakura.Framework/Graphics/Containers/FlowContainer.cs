// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A container that arranges its chldren in a defined direction
/// with wrapping them to a new line when reach the boundary.
/// </summary>
public class FlowContainer : Container
{
    private FlowDirection direction = FlowDirection.Horizontal;
    private Vector2 spacing = Vector2.Zero;

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

    public override void Update()
    {
        bool layoutWasInvalidated = (Invalidation & InvalidationFlags.DrawInfo) != 0;
        bool colourWasInvalidated = (Invalidation & InvalidationFlags.Colour) != 0;

        // run base update (from Drawable.Update())
        base.Update();

        if (!AlwaysPresent && Precision.AlmostEqualZero(Alpha))
            return;

        // calculate layout if needed
        if (layoutWasInvalidated)
        {
            PerformLayout();
        }

        // propagate after layout, because PerformLayout() might have
        // set properties on children that invalidate them (like Position).
        if (layoutWasInvalidated)
        {
            foreach (var child in Children)
            {
                child.Invalidate(InvalidationFlags.DrawInfo, false);
            }
        }

        if (colourWasInvalidated)
        {
            foreach (var child in Children)
            {
                child.Invalidate(InvalidationFlags.Colour, false);
            }
        }

        // Run update on children
        foreach (var child in Children)
        {
            child.Update();
        }
    }

    /// <summary>
    /// Calculates and applies the position for each child.
    /// </summary>
    protected virtual void PerformLayout()
    {
        // use ChildSize, which is correct and respects Padding.
        var maxBounds = ChildSize;

        float maxRight = 0;
        float maxBottom = 0;

        var currentPosPixels = Vector2.Zero;
        float lineMaxSizePixels = 0;

        foreach (var child in Children)
        {
            // must manually calculate the child's pixel size, as its
            // own .Update() hasn't run yet (so .DrawSize is from last frame).
            var childDrawSizePixels = getChildDrawSize(child);

            // calculate total size in pixels
            var childMarginPixels = child.Margin.Total;
            var childTotalSizePixels = childDrawSizePixels + childMarginPixels;

            if (Direction == FlowDirection.Horizontal)
            {
                // check for wrap (all in pixels)
                if (currentPosPixels.X > 0 && currentPosPixels.X + childTotalSizePixels.X > maxBounds.X)
                {
                    currentPosPixels.X = 0;
                    currentPosPixels.Y += lineMaxSizePixels + Spacing.Y;
                    lineMaxSizePixels = 0;
                }

                // set child's position.
                // must control these properties for layout to work.
                child.RelativePositionAxes = Axes.None; // reset to absolute positioning
                child.Anchor = Anchor.TopLeft; // Reset anchor to top-left
                child.Origin = Anchor.TopLeft; // Reset origin to top-left

                // calculate final pixel position for the child (including its margin)
                var childPosPixels = new Vector2(currentPosPixels.X + child.Margin.Left, currentPosPixels.Y + child.Margin.Top);

                // Apply position (offset by Padding)
                child.Position = new Vector2(childPosPixels.X + Padding.Left, childPosPixels.Y + Padding.Top);

                // Track content size
                float childRight = childPosPixels.X + childDrawSizePixels.X + child.Margin.Right;
                float childBottom = childPosPixels.Y + childDrawSizePixels.Y + child.Margin.Bottom;

                if (childRight > maxRight) maxRight = childRight;
                if (childBottom > maxBottom) maxBottom = childBottom;

                // Advance flow
                currentPosPixels.X += childTotalSizePixels.X + Spacing.X;
                lineMaxSizePixels = Math.Max(lineMaxSizePixels, childTotalSizePixels.Y);
            }
            else // Vertical
            {
                // check for wrap (all in pixels)
                if (currentPosPixels.Y > 0 && currentPosPixels.Y + childTotalSizePixels.Y > maxBounds.Y)
                {
                    currentPosPixels.Y = 0;
                    currentPosPixels.X += lineMaxSizePixels + Spacing.X;
                    lineMaxSizePixels = 0;
                }

                // set child's position
                child.RelativePositionAxes = Axes.None;
                child.Anchor = Anchor.TopLeft;
                child.Origin = Anchor.TopLeft;

                // calculate final pixel position for the child (including its margin)
                var childPosPixels = new Vector2(currentPosPixels.X + child.Margin.Left, currentPosPixels.Y + child.Margin.Top);

                // Apply position
                child.Position = new Vector2(childPosPixels.X + Padding.Left, childPosPixels.Y + Padding.Top);

                // Track content size
                float childRight = childPosPixels.X + childDrawSizePixels.X + child.Margin.Right;
                float childBottom = childPosPixels.Y + childDrawSizePixels.Y + child.Margin.Bottom;

                if (childRight > maxRight) maxRight = childRight;
                if (childBottom > maxBottom) maxBottom = childBottom;

                // Advance flow
                currentPosPixels.Y += childTotalSizePixels.Y + Spacing.Y;
                lineMaxSizePixels = Math.Max(lineMaxSizePixels, childTotalSizePixels.X);
            }

            if ((AutoSizeAxes & Axes.X) != 0)
                Width = maxRight + Padding.Right;

            if ((AutoSizeAxes & Axes.Y) != 0)
                Height = maxBottom + Padding.Bottom;
        }
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
            finalDrawSize.X *= parentPixelSize.X;
        if ((child.RelativeSizeAxes & Axes.Y) != 0)
            finalDrawSize.Y *= parentPixelSize.Y;

        return finalDrawSize;
    }
}
