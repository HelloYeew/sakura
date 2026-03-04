// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A <see cref="Container"/> that fills its parent while preserving a given target virtual resolution
/// according to a specific scaling strategy.
/// </summary>
public class DrawSizePreservingFillContainer : Container
{
    /// <summary>
    /// The target virtual resolution to be enforced.
    /// </summary>
    public Vector2 TargetDrawSize { get; set; } = new Vector2(1024, 768);

    /// <summary>
    /// The strategy to be used for enforcing the <see cref="TargetDrawSize"/>
    /// </summary>
    public DrawSizePreservationStrategy Strategy { get; set; } = DrawSizePreservationStrategy.Minimum;

    public DrawSizePreservingFillContainer()
    {
        // Act as a percentage-based box relative to the parent (the window)
        RelativeSizeAxes = Axes.Both;
    }

    public override void Update()
    {
        base.Update();

        if (Parent == null || Parent.ChildSize.X == 0 || Parent.ChildSize.Y == 0)
            return;

        Vector2 drawSizeRatio = new Vector2(
            Parent.ChildSize.X / TargetDrawSize.X,
            Parent.ChildSize.Y / TargetDrawSize.Y
        );

        Vector2 newScale = Vector2.One;

        switch (Strategy)
        {
            case DrawSizePreservationStrategy.Minimum:
                newScale = new Vector2(Math.Min(drawSizeRatio.X, drawSizeRatio.Y));
                break;

            case DrawSizePreservationStrategy.Maximum:
                newScale = new Vector2(Math.Max(drawSizeRatio.X, drawSizeRatio.Y));
                break;

            case DrawSizePreservationStrategy.Average:
                newScale = new Vector2(0.5f * (drawSizeRatio.X + drawSizeRatio.Y));
                break;

            case DrawSizePreservationStrategy.Separate:
                newScale = drawSizeRatio;
                break;
        }

        // Invert the scale to find the correct relative size multiplier.
        Vector2 newSize = new Vector2(
            newScale.X == 0 ? 0 : 1 / newScale.X,
            newScale.Y == 0 ? 0 : 1 / newScale.Y
        );

        if (Scale != newScale || Size != newSize)
        {
            Scale = newScale;
            Size = newSize;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }
}

/// <summary>
/// Strategies used by <see cref="DrawSizePreservingFillContainer"/> to enforce its <see cref="DrawSizePreservingFillContainer.TargetDrawSize"/>
/// </summary>
public enum DrawSizePreservationStrategy
{
    /// <summary>
    /// Preserves the aspect ratio, ensuring one axis matches the target while the other is larger. (Letterboxing without black bars)
    /// </summary>
    Minimum,

    /// <summary>
    /// Preserves the aspect ratio, ensuring one axis matches the target while the other is smaller. (Cropping)
    /// </summary>
    Maximum,

    /// <summary>
    /// Preserves the aspect ratio, acting as a compromise between Minimum and Maximum.
    /// </summary>
    Average,

    /// <summary>
    /// Disregards an aspect ratio and blindly forces the target size to fit the screen. (Stretching)
    /// </summary>
    Separate
}
