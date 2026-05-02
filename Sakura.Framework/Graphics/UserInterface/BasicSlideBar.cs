// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

public class BasicSliderBar : Container
{
    public ReactiveFloat Current { get; } = new ReactiveFloat();

    public float MinValue { get; set; } = 0f;
    public float MaxValue { get; set; } = 1f;

    // TODO: Refactor framework color to central static class
    public Color BackgroundColor { get; set; } = Color.DarkGreen;
    public Color SelectionColor { get; set; } = Color.LimeGreen;

    private readonly Box background;
    private readonly Box selection;

    public BasicSliderBar()
    {
        Size = new Vector2(200, 20);

        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = BackgroundColor
            },
            selection = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0, 1),
                Color = SelectionColor
            }
        };

        Current.ValueChanged += _ => updateSelection();
    }

    private void updateSelection()
    {
        float range = MaxValue - MinValue;
        if (range <= 0)
            return;

        float fill = Math.Clamp((Current.Value - MinValue) / range, 0f, 1f);
        selection.Size = new Vector2(fill, 1);
    }

    private void handleMouseInput(Vector2 screenSpaceMousePosition)
    {
        if (DrawRectangle.Width == 0) return;

        float localX = screenSpaceMousePosition.X - DrawRectangle.X;
        float progress = Math.Clamp(localX / DrawRectangle.Width, 0f, 1f);

        Current.Value = MinValue + progress * (MaxValue - MinValue);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        handleMouseInput(e.ScreenSpaceMousePosition);
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e) => true;

    public override bool OnDrag(MouseEvent e)
    {
        handleMouseInput(e.ScreenSpaceMousePosition);
        return true;
    }
}
