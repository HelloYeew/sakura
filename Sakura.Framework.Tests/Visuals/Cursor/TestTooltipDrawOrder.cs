// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Cursor;

/// <summary>
/// A tooltip has to draw over the content it describes. It used to sit at float.MinValue, which is
/// the BACK of the back-to-front order, leaving it hidden under opaque content and a washed-out
/// ghost under translucent content.
/// </summary>
public partial class TestTooltipDrawOrder : ManualInputManagerTestScene
{
    private TooltipContainer container = null!;
    private HoverTarget target = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create a tooltip container over opaque content", () =>
        {
            TestContent.Clear();
            TestContent.Add(container = new TooltipContainer
            {
                RelativeSizeAxes = Axes.Both,
                // Flat, not wrapped: the tooltip search only descends through ancestors that are
                // themselves hovered, so an extra wrapper container would hide the target from it.
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Color = Color.DarkSlateGray },
                    target = new HoverTarget
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(160, 40),
                        Color = Color.SteelBlue,
                        TooltipText = "I must be readable",
                    },
                },
            });
        });
    }

    [Test]
    public void TestTooltipIsTheFrontMostChild()
    {
        AddAssert("Tooltip sorts in front of the content layer", () =>
        {
            var sorted = container.SortedChildren;
            return sorted.Last() is BasicTooltip;
        });
    }

    [Test]
    public void TestTooltipOutranksAContentLayerAddedAfterIt()
    {
        // The content layer is created lazily on the first Add, i.e. after the tooltip already
        // exists. Ordering has to come from Depth, not insertion order, or this regresses silently.
        AddAssert("Tooltip still last despite being added first", () =>
        {
            var sorted = container.SortedChildren.ToList();
            return sorted.IndexOf(sorted.OfType<BasicTooltip>().Single()) == sorted.Count - 1;
        });
    }

    /// <summary>
    /// The tooltip search only considers drawables that are actually hovered, and a plain Box never
    /// reports hover, so the target has to claim it.
    /// </summary>
    private partial class HoverTarget : Box, IHasTooltip
    {
        public string? TooltipText { get; set; }

        // Deliberately does not block hover: blocking un-hovers the ancestors the tooltip search
        // walks down through, which would make this target unreachable.
        public override bool OnHover(MouseEvent e) => false;
    }
}
