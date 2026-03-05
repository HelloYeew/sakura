// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Input;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// An element which starts hidden and blocks input to drawables behind it when visible.
/// </summary>
public abstract class OverlayContainer : VisibilityContainer
{
    /// <summary>
    /// Whether we should block any mouse/touch input from interacting with things behind us.
    /// </summary>
    protected virtual bool BlockPositionalInput => true;

    public override bool OnMouseDown(MouseButtonEvent e)
        => (State == Visibility.Visible && BlockPositionalInput) || base.OnMouseDown(e);

    public override bool OnMouseUp(MouseButtonEvent e)
        => (State == Visibility.Visible && BlockPositionalInput) || base.OnMouseUp(e);

    public override bool OnMouseMove(MouseEvent e)
        => (State == Visibility.Visible && BlockPositionalInput) || base.OnMouseMove(e);

    public override bool OnScroll(ScrollEvent e)
        => (State == Visibility.Visible && BlockPositionalInput) || base.OnScroll(e);

    public override bool OnHover(MouseEvent e)
        => (State == Visibility.Visible && BlockPositionalInput) || base.OnHover(e);
}
