// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestTextureViewerDisplay : TestScene
{
    private DebugWindowLayer layer = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add the window layer", () =>
        {
            Clear();

            Add(new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "Something to fill the font atlas with",
                Font = FontUsage.Default.With(size: 24)
            });

            Add(layer = new DebugWindowLayer
            {
                Depth = float.MaxValue - 20
            });
        });
    }

    [Test]
    public void TestDisplay()
    {
        open();

        AddAssert("Window is open", () => layer.IsOpen<TextureViewerDisplay>());

        // Nothing is built in the constructor or in LoadComplete any more, so everything here waits
        // for the first refresh tick.
        AddUntilStep("Header populated", () => headerLines().Any(t => t.Text.StartsWith("Live:", StringComparison.Ordinal)));
        AddUntilStep("Cards were built", () => countCards() > 0);
    }

    [Test]
    public void TestCloseDisposes()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        TextureViewerDisplay first = null!;

        AddStep("Close", () =>
        {
            first = window();
            layer.Toggle(() => new TextureViewerDisplay());
        });

        AddAssert("Nothing left in the layer", () => layer.Children.Count == 0);
        AddAssert("The closed window was disposed", () => first.IsDisposed);

        AddStep("Reopen", () => layer.Toggle(() => new TextureViewerDisplay()));

        AddAssert("A new instance was built", () => !ReferenceEquals(window(), first));
        AddUntilStep("Cards were rebuilt", () => countCards() > 0);
    }

    [Test]
    public void TestCardContentStaysInOneColumn()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddAssert("Every card is a single column, and cannot be two", () =>
        {
            var flows = collect<FlowContainer>(window()).Where(f => f.Direction == FlowDirection.Vertical).ToArray();

            // Every vertical flow in this window is a card's label-and-preview column; the card grid
            // itself flows horizontally.
            Assert.That(flows, Is.Not.Empty);

            foreach (var flow in flows)
            {
                Assert.That(flow.AutoSizeAxes & Axes.Y, Is.EqualTo(Axes.Y), "a card's flow can wrap into a second column");

                foreach (var child in flow.Children)
                    Assert.That(child.Position.X, Is.EqualTo(flow.Children[0].Position.X).Within(0.01f), "a card's content wrapped into a second column");
            }

            return true;
        });
    }

    private void open() => AddStep("Open", () => layer.Toggle(() => new TextureViewerDisplay()));

    private TextureViewerDisplay window() => layer.OpenWindows.OfType<TextureViewerDisplay>().Single();

    private SpriteText[] headerLines() => window().Children.OfType<Container>().SelectMany(c => c.Children.OfType<SpriteText>()).ToArray();

    /// <summary>
    /// A card is the only thing in the window that previews a texture with a <see cref="Sprite"/>;
    /// the chrome and the labels are boxes and text.
    /// </summary>
    private int countCards() => countOfType<Sprite>(window());

    private static System.Collections.Generic.List<T> collect<T>(Drawable drawable) where T : Drawable
    {
        var found = new System.Collections.Generic.List<T>();

        void walk(Drawable d)
        {
            if (d is T match)
                found.Add(match);

            if (d is Container container)
            {
                foreach (var child in container.Children)
                    walk(child);
            }
        }

        walk(drawable);
        return found;
    }

    private static int countOfType<T>(Drawable drawable) where T : Drawable
    {
        int count = drawable is T ? 1 : 0;

        if (drawable is Container container)
        {
            foreach (var child in container.Children)
                count += countOfType<T>(child);
        }

        return count;
    }
}
