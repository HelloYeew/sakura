// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// Pads its content so it stays clear of the device safe area (notches, rounded corners,
/// system gesture regions) defined by the nearest ancestor <see cref="SafeAreaDefiningContainer"/>.
/// <remarks>
/// The container itself can span the full screen (e.g. a background under the notch) while its
/// children are inset to the safe region. Use <see cref="SafeAreaOverrideEdges"/> to let content
/// extend under specific edges anyway — e.g. a full-bleed playfield background with
/// <see cref="Edges.All"/>, or a bottom bar that respects only the bottom inset.
/// When no <see cref="SafeAreaDefiningContainer"/> exists in the ancestry (typical desktop
/// windowed scenarios), this behaves as a plain container with zero extra padding.
/// </remarks>
/// </summary>
public partial class SafeAreaContainer : Container
{
    private SafeAreaDefiningContainer? safeArea;

    private Edges safeAreaOverrideEdges = Edges.None;

    /// <summary>
    /// Edges on which the safe-area inset should be ignored, letting content extend
    /// into the unsafe region on those sides.
    /// </summary>
    public Edges SafeAreaOverrideEdges
    {
        get => safeAreaOverrideEdges;
        set
        {
            if (safeAreaOverrideEdges == value)
                return;

            safeAreaOverrideEdges = value;
            updatePadding();
        }
    }

    public SafeAreaContainer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        safeArea = Dependencies.TryGet<SafeAreaDefiningContainer>();
        updatePadding();
    }

    public override void Update()
    {
        base.Update();

        // Poll the defining container's insets; cheap struct compare per frame and avoids
        // event subscription lifetime management across the hierarchy.
        updatePadding();
    }

    private void updatePadding()
    {
        var insets = safeArea?.SafeAreaPadding.Value ?? new MarginPadding();

        var target = new MarginPadding
        {
            Top = (safeAreaOverrideEdges & Edges.Top) != 0 ? 0 : insets.Top,
            Left = (safeAreaOverrideEdges & Edges.Left) != 0 ? 0 : insets.Left,
            Bottom = (safeAreaOverrideEdges & Edges.Bottom) != 0 ? 0 : insets.Bottom,
            Right = (safeAreaOverrideEdges & Edges.Right) != 0 ? 0 : insets.Right,
        };

        if (!Padding.Equals(target))
            Padding = target;
    }
}
