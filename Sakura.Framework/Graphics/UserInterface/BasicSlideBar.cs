// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Graphics.UserInterface;

public class BasicSliderBar<T> : Container where T : struct, INumber<T>
{
    public Reactive<T> Current { get; } = new Reactive<T>(T.Zero);

    public T MinValue { get; set; }
    public T MaxValue { get; set; }

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
        float min = float.CreateTruncating(MinValue);
        float max = float.CreateTruncating(MaxValue);
        float current = float.CreateTruncating(Current.Value);

        float range = max - min;
        if (range <= 0) return;

        float fill = Math.Clamp((current - min) / range, 0f, 1f);
        selection.Size = new Vector2(fill, 1);
    }

    private void handleMouseInput(Vector2 screenSpaceMousePosition)
    {
        if (DrawRectangle.Width == 0) return;

        float localX = screenSpaceMousePosition.X - DrawRectangle.X;
        float progress = Math.Clamp(localX / DrawRectangle.Width, 0f, 1f);

        float min = float.CreateTruncating(MinValue);
        float max = float.CreateTruncating(MaxValue);
        float newValue = min + progress * (max - min);

        Current.Value = T.CreateTruncating(newValue);
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
