// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// Visual stress "benchmark" for <see cref="DelayedLoadUnloadWrapperContainer"/>
/// </summary>
/// TODO: Fix this man pls
[VisualTestOnly("Take a lot of time to load")]
public partial class TestDelayedLoadStress : TestScene
{
    private const int panel_count = 300;
    private const float panel_height = 74;
    private const float panel_spacing = 6;
    private const float viewport_height = 560;
    private const float viewport_width = 460;

    private Container content = null!;
    private SpriteText createdLabel = null!;
    private SpriteText loadedLabel = null!;
    private SpriteText unloadedLabel = null!;
    private SpriteText scrollLabel = null!;

    private readonly List<DelayedLoadUnloadWrapperContainer> wrappers = new List<DelayedLoadUnloadWrapperContainer>();

    private int createdCount;
    private int unloadedCount;
    private bool autoScroll = true;
    private float scrollSpeed = 800; // px/s
    private float scrollDirection = 1;

    private static float contentHeight => panel_count * (panel_height + panel_spacing);

    [SetUp]
    public void SetUp()
    {
        AddStep("Create carousel", () =>
        {
            Clear();
            wrappers.Clear();
            createdCount = 0;
            unloadedCount = 0;
            scrollDirection = 1;

            Add(new Container
            {
                Position = new Vector2(12, 12),
                AutoSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    createdLabel = makeLabel(0),
                    loadedLabel = makeLabel(1),
                    unloadedLabel = makeLabel(2),
                    scrollLabel = makeLabel(3),
                }
            });

            var viewport = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(viewport_width, viewport_height),
                Masking = true,
                BorderThickness = 3,
                BorderColor = Color.White,
            };

            viewport.Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.DarkSlateGray
            });

            viewport.Add(content = new Container
            {
                Size = new Vector2(viewport_width, contentHeight)
            });

            for (int i = 0; i < panel_count; i++)
            {
                int index = i;
                var wrapper = new DelayedLoadUnloadWrapperContainer(() => makePanel(index), 120, 800)
                {
                    Size = new Vector2(viewport_width - 24, panel_height),
                    Position = new Vector2(12, i * (panel_height + panel_spacing))
                };

                wrapper.ContentUnloaded += () => unloadedCount++;

                wrappers.Add(wrapper);
                content.Add(wrapper);
            }

            Add(viewport);
        });
    }

    private static SpriteText makeLabel(int line) => new SpriteText
    {
        Text = string.Empty,
        Position = new Vector2(0, line * 24),
        Color = Color.White
    };

    private Drawable makePanel(int index)
    {
        createdCount++;

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = index % 2 == 0 ? Color.SteelBlue : Color.SeaGreen
                },
                new Box
                {
                    Size = new Vector2(6, panel_height),
                    Color = Color.Goldenrod
                },
                new SpriteText
                {
                    Text = $"Panel #{index}",
                    Position = new Vector2(14, 8),
                    Color = Color.White
                },
                new SpriteText
                {
                    Text = "Artist — Title [Difficulty]",
                    Position = new Vector2(14, 38),
                    Color = Color.LightGray
                }
            }
        };
    }

    [Test]
    public void TestStress()
    {
        AddStep("Toggle auto-scroll", () => autoScroll = !autoScroll);
        AddSliderStep("Scroll speed (px/s)", 100f, 8000f, 800f, v => scrollSpeed = v);
        AddStep("Teleport to bottom", () => content.Position = new Vector2(0, -(contentHeight - viewport_height)));
        AddStep("Teleport to top", () => content.Position = new Vector2(0, 0));
        AddUntilStep("Some panels load", () => createdCount > 0, 30000);
    }

    public override void Update()
    {
        base.Update();

        if (content == null)
            return;

        if (autoScroll)
        {
            float maxScroll = contentHeight - viewport_height;
            float y = content.Position.Y - scrollDirection * scrollSpeed * (float)(Clock.ElapsedFrameTime / 1000.0);

            if (y < -maxScroll)
            {
                y = -maxScroll;
                scrollDirection = -1;
            }
            else if (y > 0)
            {
                y = 0;
                scrollDirection = 1;
            }

            content.Position = new Vector2(content.Position.X, y);
        }

        int loaded = 0;

        foreach (var w in wrappers)
        {
            if (w.DelayedLoadCompleted)
                loaded++;
        }

        createdLabel.Text = $"Panels created (total): {createdCount} / {panel_count}";
        loadedLabel.Text = $"Currently loaded: {loaded}";
        unloadedLabel.Text = $"Unloaded: {unloadedCount}";
        scrollLabel.Text = $"Auto-scroll: {(autoScroll ? "on" : "off")}, speed: {scrollSpeed:F0} px/s, pos: {-content.Position.Y:F0}/{contentHeight - viewport_height:F0}";
    }
}
