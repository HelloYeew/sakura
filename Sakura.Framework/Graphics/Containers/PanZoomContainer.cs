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
public class PanZoomContainer : Container
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
    public override void Remove(Drawable drawable) => contentLayer.Remove(drawable);
    public override void Clear() => contentLayer.Clear();

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

        if (e.Button == MouseButton.Middle || e.Button == MouseButton.Right)
        {
            isPanning = true;
            return true;
        }

        return false;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if ((e.Button == MouseButton.Middle || e.Button == MouseButton.Right) && isPanning)
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

        if (isPanning)
        {
            contentLayer.Position += e.Delta;
            handled = true;
        }

        return handled;
    }
}
