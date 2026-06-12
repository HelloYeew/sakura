// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// Defines the device safe area for its subtree.
/// <para>
/// Place one of these near the root of the scene (covering the whole window). It tracks the
/// window's safe-area insets (notches, rounded corners, system gesture areas) and exposes them
/// to descendants via dependency injection. Any <see cref="SafeAreaContainer"/> below it will
/// pad its content to stay clear of those areas.
/// </para>
/// <para>
/// In short: this container <b>provides</b> the insets; <see cref="SafeAreaContainer"/>
/// <b>consumes</b> them. They are separate so that one screen-filling definition can serve any
/// number of consumers, and so consumers can individually opt out of edges
/// (<see cref="SafeAreaContainer.SafeAreaOverrideEdges"/>) to draw backgrounds under the notch
/// while keeping UI inside it.
/// </para>
/// <remarks>
/// This container is assumed to cover the full window; the window's insets are passed through
/// without re-mapping. Use <see cref="OverrideSafeArea"/> to simulate insets on desktop
/// (e.g. in visual tests) or to supply custom values.
/// </remarks>
/// </summary>
[Cached]
public partial class SafeAreaDefiningContainer : Container
{
    /// <summary>
    /// The current safe-area insets observed by this container, in local (window) pixels.
    /// </summary>
    public Reactive<MarginPadding> SafeAreaPadding { get; } = new Reactive<MarginPadding>(new MarginPadding());

    private MarginPadding? overrideSafeArea;

    /// <summary>
    /// When set, these insets are used instead of the window's reported safe area.
    /// Useful for visual tests and desktop simulation. Set to null to resume following the window.
    /// </summary>
    public MarginPadding? OverrideSafeArea
    {
        get => overrideSafeArea;
        set
        {
            overrideSafeArea = value;
            updatePadding();
        }
    }

    private IWindow? window;

    public SafeAreaDefiningContainer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        window = Dependencies.TryGet<IWindow>();
        updatePadding();
    }

    public override void Update()
    {
        base.Update();

        // Poll rather than subscribe: window reactives fire on the main thread, while the
        // scene graph lives on the update thread. A per-frame struct compare is cheap.
        updatePadding();
    }

    private void updatePadding()
    {
        var target = overrideSafeArea ?? window?.SafeAreaPadding.Value ?? new MarginPadding();

        if (!SafeAreaPadding.Value.Equals(target))
            SafeAreaPadding.Value = target;
    }
}
