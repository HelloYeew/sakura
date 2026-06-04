// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

public class BasicDropdown<T> : Container
{
    public Reactive<T> Current { get; } = new Reactive<T>(default!);

    private readonly BasicButton header;
    private readonly Container menu;

    private IEnumerable<T> items = Enumerable.Empty<T>();
    public IEnumerable<T> Items
    {
        get => items;
        set
        {
            items = value;
            generateMenuItems();
        }
    }

    public BasicDropdown()
    {
        AutoSizeAxes = Axes.Y;
        Width = 200;

        Children = new Drawable[]
        {
            header = new BasicButton
            {
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Height = 30,
                Text = "Select...",
                Action = toggleMenu,
                TextAnchor = Anchor.CentreLeft
            },
            menu = new Container
            {
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Y = 30,
                Alpha = 0
            }
        };

        header.Text = Current.Value?.ToString() ?? "Select...";
    }

    public override void LoadComplete()
    {
        base.LoadComplete();

        Current.ValueChanged += e =>
        {
            header.Text = e.NewValue?.ToString() ?? "Select...";
        };
    }

    private void toggleMenu()
    {
        if (menu.IsHidden)
            menu.Show();
        else
            menu.Hide();
    }

    private void generateMenuItems()
    {
        menu.Clear();
        float currentY = 0;

        foreach (var item in Items)
        {
            var capturedItem = item;
            var dropdownItem = new BasicButton
            {
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Height = 30,
                Y = currentY,
                Text = capturedItem?.ToString() ?? "",
                Action = () =>
                {
                    Current.Value = capturedItem;
                    menu.Hide();
                },
                TextAnchor = Anchor.CentreLeft
            };

            menu.Add(dropdownItem);
            currentY += 30;
        }

        menu.Height = currentY;
    }
}
