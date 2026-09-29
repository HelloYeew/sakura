// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Cursor;

/// <summary>
/// When two tooltip-bearing controls overlap, the one actually in front wins.
/// </summary>
/// <remarks>
/// The search walked <c>Children</c>, whose reverse order is only "front-most first" while no child
/// has been given a <c>Depth</c> — so a raised control lost its tooltip to whichever sibling merely
/// happened to be added later. It now walks <c>SortedChildren</c>, the same ordering
/// <c>InputManager.buildPositional</c> uses.
/// </remarks>
public partial class TestTooltipSearchOrder : ManualInputManagerTestScene
{
    private TooltipContainer tooltipContainer;
    private Container overlap;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build two overlapping tooltips", () =>
        {
            TestContent.Clear();

            TestContent.Add(tooltipContainer = new TooltipContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = overlap = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(200),
                    Children = new Drawable[]
                    {
                        // Added FIRST but raised to the front by Depth. Insertion order and depth
                        // order disagree, which is the whole point of the fixture.
                        new TooltipBox("front") { RelativeSizeAxes = Axes.Both, Depth = 10 },
                        new TooltipBox("behind") { RelativeSizeAxes = Axes.Both, Depth = 0 },
                    },
                },
            });
        });
    }

    [Test]
    public void TestTheFrontMostTooltipWins()
    {
        AddStep("Hover the overlap", () => InputManager.MoveMouseTo(overlap));

        AddUntilStep("The raised control's tooltip shows", () =>
            DrawableQuery(tooltipContainer).Any(t => t.Text == "front"));
    }

    private static System.Collections.Generic.IEnumerable<SpriteText> DrawableQuery(Drawable root)
    {
        if (root is SpriteText text)
            yield return text;

        if (root is not Container container)
            yield break;

        foreach (var child in container.InternalChildren)
        {
            foreach (var found in DrawableQuery(child))
                yield return found;
        }
    }

    private partial class TooltipBox : Container, IHasTooltip
    {
        public string TooltipText { get; }

        public TooltipBox(string tooltip)
        {
            TooltipText = tooltip;
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.SlateGray, Alpha = 0.4f });
        }
    }
}
