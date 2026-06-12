// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestDelayedLoadWrapperContainer : TestScene
{
    private const float viewport_size = 420;
    private static readonly Vector2 inside_position = new Vector2(10, 10);
    private static readonly Vector2 outside_position = new Vector2(10, viewport_size + 200);

    private Container viewport = null!;
    private DelayedLoadWrapperContainer wrapperContainer = null!;
    private DelayedLoadUnloadWrapperContainer unloadWrapperContainer = null!;
    private SpriteText status = null!;

    private int creationCount;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create viewport", () =>
        {
            Clear();
            creationCount = 0;

            Add(status = new SpriteText
            {
                Text = "Created panels: 0",
                Position = new Vector2(12, 12),
                Color = Color.White
            });

            Add(viewport = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(viewport_size),
                Masking = true,
                BorderThickness = 3,
                BorderColor = Color.White,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkSlateGray
                    },
                    wrapperContainer = new DelayedLoadWrapperContainer(() => makePanel("DelayedLoadWrapper content", Color.SeaGreen), 300)
                    {
                        Size = new Vector2(380, 90),
                        Position = outside_position
                    },
                    unloadWrapperContainer = new DelayedLoadUnloadWrapperContainer(() => makePanel("DelayedLoadUnloadWrapper content", Color.Goldenrod), 300, 600)
                    {
                        Size = new Vector2(380, 90),
                        Position = new Vector2(10, viewport_size + 320)
                    }
                }
            });
        });
    }

    private Drawable makePanel(string label, Color color)
    {
        creationCount++;

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = color
                },
                new SpriteText
                {
                    Text = $"{label} (instance #{creationCount})",
                    Position = new Vector2(8, 8),
                    Color = Color.White
                }
            }
        };
    }

    public override void Update()
    {
        base.Update();

        if (status != null)
            status.Text = $"Created panels: {creationCount}";
    }

    [Test]
    public void TestDeferredLoad()
    {
        AddAssert("Not loaded while off screen", () => !wrapperContainer.DelayedLoadCompleted);
        AddWaitStep("Stay off screen", 500);
        AddAssert("Still not loaded", () => !wrapperContainer.DelayedLoadCompleted);

        AddStep("Move into view", () => wrapperContainer.Position = inside_position);
        AddUntilStep("Content loads after delay", () => wrapperContainer.DelayedLoadCompleted);

        AddStep("Move out of view", () => wrapperContainer.Position = outside_position);
        AddWaitStep("Wait", 800);
        AddAssert("Plain wrapper keeps content", () => wrapperContainer.DelayedLoadCompleted);
    }

    [Test]
    public void TestUnloadAndReload()
    {
        AddStep("Move unload wrapper into view", () => unloadWrapperContainer.Position = new Vector2(10, 120));
        AddUntilStep("Content loads", () => unloadWrapperContainer.DelayedLoadCompleted);

        AddStep("Move out of view", () => unloadWrapperContainer.Position = outside_position);
        AddUntilStep("Content unloads after delay", () => !unloadWrapperContainer.DelayedLoadCompleted);

        AddStep("Move back into view", () => unloadWrapperContainer.Position = new Vector2(10, 120));
        AddUntilStep("Content recreated", () => unloadWrapperContainer.DelayedLoadCompleted);
        AddAssert("A new instance was created", () => creationCount >= 2);
    }

    [Test]
    public void TestQuickScrollPastDoesNotLoad()
    {
        AddStep("Briefly through view", () =>
        {
            wrapperContainer.Position = inside_position;
        });
        AddStep("Immediately out again", () => wrapperContainer.Position = outside_position);
        AddWaitStep("Wait past load delay", 600);
        AddAssert("Never loaded", () => !wrapperContainer.DelayedLoadCompleted);
    }
}
