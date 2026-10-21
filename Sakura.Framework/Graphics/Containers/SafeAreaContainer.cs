// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// Pads its content by however much of this container lies outside the safe area defined by the nearest
/// ancestor <see cref="SafeAreaDefiningContainer"/>, edge by edge and in its own units, so it works at any
/// depth and under any scale.
/// <remarks>
/// <see cref="App"/> content is already safe, so this pads nothing there. It's for content inside
/// something that bleeds (<see cref="Drawable.Bleed"/>): a bar whose background reaches under the notch
/// keeps its buttons safe by holding them in one of these. Use <see cref="SafeAreaOverrideEdges"/> to let
/// content extend under specific edges anyway. With no <see cref="SafeAreaDefiningContainer"/> above it,
/// this is a plain container.
/// </remarks>
/// </summary>
public partial class SafeAreaContainer : Container
{
    private Edges safeAreaOverrideEdges = Edges.None;

    /// <summary>
    /// Edges on which the safe area is ignored, letting content extend into the unsafe region on those sides.
    /// </summary>
    public Edges SafeAreaOverrideEdges
    {
        get => safeAreaOverrideEdges;
        set
        {
            if (safeAreaOverrideEdges == value)
                return;

            safeAreaOverrideEdges = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public SafeAreaContainer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    protected internal override void UpdateTransforms()
    {
        base.UpdateTransforms();

        // The definition above has already been laid out this frame, and it re-invalidates everything
        // below it whenever its safe area changes, so this is the only place the padding needs working out.
        var definition = SafeAreaDefiningContainer.FindFor(this);

        var insets = definition is { HasSafeArea: true }
            ? SafeAreaDefiningContainer.PaddingWithin(this, definition.ScreenSpaceSafeArea)
            : new MarginPadding();

        var target = new MarginPadding
        {
            Top = (safeAreaOverrideEdges & Edges.Top) != 0 ? 0 : insets.Top,
            Left = (safeAreaOverrideEdges & Edges.Left) != 0 ? 0 : insets.Left,
            Bottom = (safeAreaOverrideEdges & Edges.Bottom) != 0 ? 0 : insets.Bottom,
            Right = (safeAreaOverrideEdges & Edges.Right) != 0 ? 0 : insets.Right,
        };

        if (target.Equals(Padding))
            return;

        Padding = target;

        var children = InternalChildren;
        for (int i = 0; i < children.Count; i++)
            children[i].Invalidate(InvalidationFlags.DrawInfo, false);
    }
}
