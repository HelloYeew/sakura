// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestContainerAutoSizeEasing : TestScene
{
    private Container autoSizeContainer = null!;
    private Box background = null!;
    private readonly List<Box> contentBoxes = new List<Box>();

    [SetUp]
    public void SetUp()
    {
        AddStep("Create auto-size container", () =>
        {
            contentBoxes.Clear();

            autoSizeContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                AutoSizeDuration = 500,
                AutoSizeEasing = Easing.OutQuint,
                Padding = new MarginPadding(10),
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkSlateGray
                    },
                    new Box
                    {
                        Size = new Vector2(100),
                        Color = Color.Red
                    }
                }
            };
        });

        AddStep("Add container", () => Add(autoSizeContainer));
    }

    [Test]
    public void TestGrowAndShrink()
    {
        AddUntilStep("Container settles at 120x120", () => autoSizeContainer.Size == new Vector2(120));

        AddStep("Add a wide child", () =>
        {
            var box = new Box
            {
                Y = 100,
                Size = new Vector2(260, 60),
                Color = Color.SkyBlue
            };
            contentBoxes.Add(box);
            autoSizeContainer.Add(box);
        });

        AddUntilStep("Container grew to fit wide child", () => autoSizeContainer.Size == new Vector2(280, 180));

        AddStep("Remove the wide child", () =>
        {
            var box = contentBoxes[^1];
            contentBoxes.Remove(box);
            autoSizeContainer.Remove(box);
        });

        AddUntilStep("Container shrank back to 120x120", () => autoSizeContainer.Size == new Vector2(120));
    }

    [Test]
    public void TestInstantWhenZeroDuration()
    {
        AddStep("Set duration to 0", () => autoSizeContainer.AutoSizeDuration = 0);

        AddStep("Add a tall child", () =>
        {
            var box = new Box
            {
                Y = 100,
                Size = new Vector2(80, 200),
                Color = Color.Orange
            };
            contentBoxes.Add(box);
            autoSizeContainer.Add(box);
        });

        AddUntilStep("Container snapped to new height", () => autoSizeContainer.Size == new Vector2(120, 320));
    }

    [Test]
    public void TestEasingPlayground()
    {
        AddSliderStep("Auto-size duration", 0d, 2000d, 500d, v => autoSizeContainer.AutoSizeDuration = v);

        addEasingStep(Easing.None);
        addEasingStep(Easing.OutQuint);
        addEasingStep(Easing.OutBack);
        addEasingStep(Easing.OutElastic);
    }

    private void addEasingStep(Easing easing)
    {
        AddStep($"Easing: {easing} + toggle size", () =>
        {
            autoSizeContainer.AutoSizeEasing = easing;

            if (contentBoxes.Count > 0)
            {
                var box = contentBoxes[^1];
                contentBoxes.Remove(box);
                autoSizeContainer.Remove(box);
            }
            else
            {
                var box = new Box
                {
                    X = 100,
                    Size = new Vector2(200, 100),
                    Color = Color.MediumPurple
                };
                contentBoxes.Add(box);
                autoSizeContainer.Add(box);
            }
        });
    }

    [TearDown]
    public void TearDown()
    {
        AddStep("Clear all children", Clear);
    }
}
