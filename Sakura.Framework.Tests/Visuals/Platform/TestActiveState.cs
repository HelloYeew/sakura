// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Platform;

public class TestActiveState : TestScene
{
    [Resolved]
    private IWindow window { get; set; }

    private readonly Box activeBox = new Box
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both
    };

    private readonly Box cursorInWindowBox = new Box
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both
    };

    [OneTimeSetUp]
    public void SetUp()
    {
        AddStep("Add active box", () =>
        {
            Add(new Container()
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.5f, 1),
                Children = new Drawable[]
                {
                    activeBox,
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "window.IsActive",
                        Color = Color.White
                    }
                }
            });

            Add(new Container()
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.5f, 1),
                Children = new Drawable[]
                {
                    cursorInWindowBox,
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "window.CursorInWindow",
                        Color = Color.White
                    }
                }
            });
        });
    }

    public override void Update()
    {
        base.Update();
        activeBox.Color = window.IsActive ? Color.Green : Color.Red;
        cursorInWindowBox.Color = window.CursorInWindow ? Color.Green : Color.Red;
    }
}
