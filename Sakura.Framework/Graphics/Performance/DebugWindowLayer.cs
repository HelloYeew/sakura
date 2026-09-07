// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// Hosts the <see cref="DebugWindow"/>s. Builds a tool the first time it is opened and detaches it
/// from the drawable tree when it is closed.
/// </summary>
public partial class DebugWindowLayer : Container, IRemoveFromDrawVisualiser
{
    /// <summary>
    /// Offset applied to each successive first-time open, so a freshly opened window is not hidden
    /// exactly behind the one before it.
    /// </summary>
    private const float cascade_step = 28;

    private const float cascade_origin = 40;

    private readonly Dictionary<Type, DebugWindow> instances = new Dictionary<Type, DebugWindow>();

    /// <summary>
    /// Where each window was when it was last closed, so reopening puts it back rather than resetting
    /// it to the cascade position. Session-only, deliberately not persisted to configuration.
    /// </summary>
    private readonly Dictionary<Type, (Vector2 Position, Vector2 Size)> storedBounds = new Dictionary<Type, (Vector2, Vector2)>();

    private int cascadeIndex;
    private float topDepth;

    public DebugWindowLayer()
    {
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
    }

    /// <summary>
    /// The windows currently open, front-most last.
    /// </summary>
    public IEnumerable<DebugWindow> OpenWindows
    {
        get
        {
            foreach (var child in Children)
            {
                if (child is DebugWindow window)
                    yield return window;
            }
        }
    }

    public bool IsOpen<T>() where T : DebugWindow
        => instances.TryGetValue(typeof(T), out var window) && window.Parent != null;

    /// <summary>
    /// Opens the window if it is closed, closes it if it is open. <paramref name="factory"/> runs only
    /// on the first open, and never at all if the tool is never used.
    /// </summary>
    public void Toggle<T>(Func<T> factory) where T : DebugWindow
    {
        if (IsOpen<T>())
            Close<T>();
        else
            Open(factory);
    }

    /// <summary>
    /// Opens the window or brings it to the front if it is already open.
    /// </summary>
    public T Open<T>(Func<T> factory) where T : DebugWindow
    {
        if (!instances.TryGetValue(typeof(T), out var window) || window.IsDisposed)
        {
            window = factory();
            window.CloseRequested += close;
            window.RaiseRequested += raise;
            instances[typeof(T)] = window;
        }

        if (window.Parent == null)
        {
            Add(window);

            if (storedBounds.TryGetValue(typeof(T), out var bounds))
                window.SetBounds(bounds.Position, bounds.Size);
            else
                window.SetBounds(nextCascadePosition(), window.CurrentSize);

            window.OnOpened();
        }

        raise(window);

        return (T)window;
    }

    public void Close<T>() where T : DebugWindow
    {
        if (instances.TryGetValue(typeof(T), out var window))
            close(window);
    }

    public void CloseAll()
    {
        // Snapshot: closing mutates the child list.
        foreach (var window in new List<DebugWindow>(OpenWindows))
            close(window);
    }

    /// <summary>
    /// Brings the window under <paramref name="screenSpacePosition"/> to the front, if any. Meant to be
    /// called by whoever sees a mouse-down before it is dispatched (the app root). Dispatch
    /// reaches a window's children before the window itself so a click on a window's content cannot
    /// be observed by the window, and raising cannot be done from a handler without every piece of
    /// content cooperating.
    /// </summary>
    /// <returns>Whether a window was under the position.</returns>
    public bool NotifyMouseDown(Vector2 screenSpacePosition)
    {
        DebugWindow? hit = null;

        foreach (var window in OpenWindows)
        {
            if (window.Contains(screenSpacePosition) && (hit == null || window.Depth > hit.Depth))
                hit = window;
        }

        if (hit == null)
            return false;

        raise(hit);
        return true;
    }

    /// <summary>
    /// Adds a screen-space companion to a window: something that has to draw over the app instead of
    /// being clipped into a window's body, and to reach drawables anywhere on screen.
    /// Mainly for <see cref="DrawVisualiser"/>'s highlight boxes and its inspected picker.
    /// </summary>
    /// <remarks>
    /// An overlay keeps depth 0 while windows are raised to increasing depths, so it always sits
    /// behind every window — a highlight drawn over the app must not cover the window describing it.
    /// Overlays are not <see cref="DebugWindow"/>s, so they are invisible to <see cref="OpenWindows"/>
    /// and to everything that iterates it.
    /// </remarks>
    public void AttachOverlay(Drawable overlay)
    {
        if (overlay.Parent == this)
            return;

        Add(overlay);
    }

    public void DetachOverlay(Drawable overlay)
    {
        if (overlay.Parent == this)
            Remove(overlay, dispose: false);
    }

    private void close(DebugWindow window)
    {
        if (window.Parent == null)
            return;

        storedBounds[window.GetType()] = (window.Position, window.CurrentSize);

        window.OnClosed();

        bool dispose = window.DisposeOnClose;

        Remove(window, dispose);

        if (dispose)
            instances.Remove(window.GetType());
    }

    /// <summary>
    /// The highest depth is drawn last and receives input first, so raising is a depth bump. Depth is
    /// settable at runtime and re-sorts the parent's children on the next frame.
    /// </summary>
    private void raise(DebugWindow window)
    {
        if (window.Parent != this)
            return;

        if (Precision.AlmostEquals(window.Depth, topDepth) && topDepth > 0)
            return;

        window.Depth = ++topDepth;
    }

    private Vector2 nextCascadePosition()
    {
        float offset = cascade_origin + cascade_step * (cascadeIndex++ % 6);
        return new Vector2(offset, offset);
    }
}
