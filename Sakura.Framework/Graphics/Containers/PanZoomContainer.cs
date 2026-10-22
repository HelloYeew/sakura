// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A container that allows panning and zooming of its content.
/// Useful for things like node editors or maps.
/// </summary>
/// <remarks>
/// With a mouse, the right or middle button drags and the wheel zooms. With touch, a finger dragged over the
/// background drags, and two fingers pinch to zoom and pan together.
/// </remarks>
public partial class PanZoomContainer : Container
{
    /// <summary>
    /// The minimum zoom level. Values below this will be clamped. Default is 0.25 (25%).
    /// </summary>
    public float MinZoom { get; set; } = 0.25f;

    /// <summary>
    /// The maximum zoom level. Values above this will be clamped. Default is 3.0 (300%).
    /// </summary>
    public float MaxZoom { get; set; } = 3.0f;

    /// <summary>
    /// The speed at which zooming occurs when scrolling. The default is 0.1 (10% per scroll tick).
    /// </summary>
    public float ZoomSpeed { get; set; } = 0.1f;

    private bool isPanning;
    private MouseButton panButton;

    // The pinch in progress: the finger this took, the other finger, and where they started.
    private TouchSource? pinchFinger;
    private TouchSource pinchAnchor;
    private bool pinching;
    private float pinchStartDistance;
    private float pinchStartZoom;
    private Vector2 pinchContentPoint;

    private readonly Container contentLayer;

    /// <summary>
    /// The current zoom level. Setting this will clamp the value between <see cref="MinZoom"/> and <see cref="MaxZoom"/>.
    /// </summary>
    public float Zoom
    {
        get => contentLayer.Scale.X;
        set
        {
            float clampedZoom = Math.Clamp(value, MinZoom, MaxZoom);
            contentLayer.Scale = new Vector2(clampedZoom);
        }
    }

    /// <summary>
    /// The current pan position (offset) of the content. Setting this will move the content layer accordingly.
    /// </summary>
    public Vector2 PanPosition
    {
        get => contentLayer.Position;
        set => contentLayer.Position = value;
    }

    public PanZoomContainer()
    {
        Masking = true;

        // TODO: This should ideally be dynamic based on the content size, but for now we can just use a very large area to allow free panning.
        base.Add(contentLayer = new Container()
        {
            Size = new Vector2(1000000, 1000000),
            Position = new Vector2(-500000, -500000)
        });
    }

    public override void Add(Drawable drawable) => contentLayer.Add(drawable);
    public override void Remove(Drawable drawable, bool dispose = true) => contentLayer.Remove(drawable, dispose);
    public override void Clear(bool dispose = true) => contentLayer.Clear(dispose);

    public override bool OnScroll(ScrollEvent e)
    {
        // Let children (e.g., scrollable text boxes inside a node) handle scroll first
        if (base.OnScroll(e))
            return true;

        float zoomDelta = e.ScrollDelta.Y * ZoomSpeed;
        float newZoom = Math.Clamp(Zoom + zoomDelta, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - Zoom) > 0.001f)
        {
            // Zoom towards the cursor by factoring the mouse's offset from the content layer's position
            Vector2 localMousePos = e.ScreenSpaceMousePosition - contentLayer.Position;
            float scaleFactor = newZoom / Zoom;

            contentLayer.Position -= new Vector2(
                localMousePos.X * (scaleFactor - 1f),
                localMousePos.Y * (scaleFactor - 1f)
            );

            Zoom = newZoom;
            return true;
        }

        return false;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        // Let children handle mouse down first (e.g., clicking a node)
        if (base.OnMouseDown(e)) return true;

        // A finger has no other buttons, so its drag over the background pans.
        if (e.Button == MouseButton.Middle || e.Button == MouseButton.Right || e.Button == MouseButton.Left && e.MouseState.FromTouch)
        {
            isPanning = true;
            panButton = e.Button;
            return true;
        }

        return false;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if (isPanning && e.Button == panButton)
        {
            isPanning = false;
            return true;
        }

        return base.OnMouseUp(e);
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        // Let children handle mouse moves first
        bool handled = base.OnMouseMove(e);

        // While two fingers pinch, the first one (which is the mouse) only moves as part of the pinch.
        if (isPanning && !pinching)
        {
            contentLayer.Position += e.Delta;
            handled = true;
        }

        return handled;
    }

    public override bool OnTouchDown(TouchEvent e)
    {
        var manager = GetContainingInputManager();

        // Only the second finger of a gesture, and only while the first is on this too.
        if (pinchFinger != null || manager == null || manager.CurrentState.ActiveTouchCount != 2)
            return false;

        for (int i = 0; i < InputState.MAX_TOUCHES; i++)
        {
            var other = (TouchSource)i;

            if (other == e.Source || !manager.CurrentState.IsTouchActive(other) || !Contains(manager.CurrentState.GetTouchPosition(other)))
                continue;

            pinchFinger = e.Source;
            pinchAnchor = other;
            beginPinch(manager.CurrentState.GetTouchPosition(other), e.ScreenSpacePosition);
            return true;
        }

        return false;
    }

    public override bool OnTouchMove(TouchEvent e)
    {
        if (e.Source != pinchFinger)
            return false;

        // The other finger lifted first, so this one carries on alone and pans.
        if (!pinching)
            contentLayer.Position += ToLocalSpace(e.ScreenSpacePosition) - ToLocalSpace(e.ScreenSpacePosition - e.Delta);

        return true;
    }

    public override bool OnTouchUp(TouchEvent e)
    {
        if (e.Source != pinchFinger)
            return false;

        pinchFinger = null;
        pinching = false;
        return true;
    }

    public override void Update()
    {
        base.Update();

        if (!pinching)
            return;

        var state = GetContainingInputManager()?.CurrentState;

        if (state == null || !state.IsTouchActive(pinchAnchor))
        {
            pinching = false;
            return;
        }

        // Both fingers are read here rather than from their events: the first finger's moves go to the mouse.
        Vector2 a = ToLocalSpace(state.GetTouchPosition(pinchAnchor));
        Vector2 b = ToLocalSpace(state.GetTouchPosition(pinchFinger!.Value));
        float distance = Vector2.Distance(a, b);

        if (pinchStartDistance > 0)
            Zoom = pinchStartZoom * distance / pinchStartDistance;

        // What was under the point between the fingers when they started stays under it.
        contentLayer.Position = (a + b) / 2 - pinchContentPoint * Zoom;
    }

    private void beginPinch(Vector2 screenSpaceA, Vector2 screenSpaceB)
    {
        Vector2 a = ToLocalSpace(screenSpaceA);
        Vector2 b = ToLocalSpace(screenSpaceB);

        pinching = true;
        pinchStartDistance = Vector2.Distance(a, b);
        pinchStartZoom = Zoom;
        pinchContentPoint = ((a + b) / 2 - contentLayer.Position) / Zoom;
    }
}
