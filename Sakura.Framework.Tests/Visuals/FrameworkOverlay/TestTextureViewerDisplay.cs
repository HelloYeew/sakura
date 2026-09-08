// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestTextureViewerDisplay : TestScene
{
    private DebugWindowLayer layer = null!;

    /// <summary>
    /// Named, differently sized textures to sort and filter, since nothing else in a headless scene
    /// carries a name worth ordering by. Disposed at teardown: they are in the global
    /// <see cref="TextureRegistry"/> for as long as they live, and every other test that counts
    /// textures would see them.
    /// </summary>
    private readonly List<Texture> probes = new List<Texture>();

    [TearDown]
    public void DisposeProbes()
    {
        foreach (var texture in probes)
            texture.Dispose();

        probes.Clear();
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("Add the window layer", () =>
        {
            Clear();

            probes.Add(new Texture(new HeadlessNativeTexture(128, 128)) { Name = "gamma-probe" });
            probes.Add(new Texture(new HeadlessNativeTexture(64, 64)) { Name = "alpha-probe" });
            probes.Add(new Texture(new HeadlessNativeTexture(32, 32)) { Name = "beta-probe" });

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

        AddUntilStep("Nothing left in the layer once the close animation ends", () => layer.Children.Count == 0);
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

    /// <summary>
    /// The header says what this window is for and nothing else. The per-category native memory
    /// breakdown it used to carry — tex, fb, video, audio, fonts, other, mapped — was the "Native
    /// Memory" group of Ctrl+F2 rendered a second time. The total stays, because the texture total
    /// above it is what wants reading against it.
    /// </summary>
    [Test]
    public void TestHeaderDoesNotRepeatTheStatisticsOverlay()
    {
        open();
        AddUntilStep("Header populated", () => headerLines().Any(t => t.Text.StartsWith("Live:", StringComparison.Ordinal)));

        AddAssert("Two header lines", () => headerLines().Length == 2);
        AddAssert("The native total is still here", () => headerLines().Any(t => t.Text.Contains("Native: ", StringComparison.Ordinal)));

        AddAssert("But not the breakdown", () => headerLines().All(t =>
            !t.Text.Contains("   fb ", StringComparison.Ordinal)
            && !t.Text.Contains("   audio ", StringComparison.Ordinal)
            && !t.Text.Contains("mapped ", StringComparison.Ordinal)));
    }

    [Test]
    public void TestFilterNarrowsTheList()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddAssert("Nothing is held back to start with", () => window().ShownCards == window().TotalCards);

        AddStep("Filter to one probe", () => window().SearchText.Value = "alpha-probe");
        AddUntilStep("The list narrowed", () => window().ShownCards < window().TotalCards);

        AddAssert("Only the matching probe is left", () => window().DisplayedTextures.Count == 1 && window().DisplayedTextures[0].Name == "alpha-probe");
        AddAssert("The header says what is held back", () => headerLines().Any(t => t.Text.Contains("showing ", StringComparison.Ordinal)));

        AddStep("Clear the filter", () => window().SearchText.Value = string.Empty);

        // Against TotalCards rather than a count remembered from before the filter: the font atlas can
        // gain a page while the test runs, and then the list comes back one card larger than it went.
        AddUntilStep("Everything is back", () => window().ShownCards == window().TotalCards);
        AddAssert("And that is more than the one match", () => window().ShownCards > 1);
    }

    /// <summary>
    /// The filter reads a card's whole title, not just its name, so the dimensions in it are
    /// searchable too — which is how you find every 1024-wide texture at once.
    /// </summary>
    [Test]
    public void TestFilterMatchesDimensions()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddStep("Filter by a size", () => window().SearchText.Value = "128x128");
        AddUntilStep("The filter applied", () => window().ShownCards < window().TotalCards);

        AddAssert("The 128x128 probe survived", () => window().DisplayedTextures.Any(t => t.Name == "gamma-probe"));
        AddAssert("The 32x32 probe did not", () => window().DisplayedTextures.All(t => t.Name != "beta-probe"));
    }

    [Test]
    public void TestFilterMatchingNothingExplainsItself()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddStep("Filter to nothing", () => window().SearchText.Value = "no-texture-is-called-this");
        AddUntilStep("Every card went", () => window().ShownCards == 0);

        AddAssert("No cards are left", () => countCards() == 0);
        AddAssert("Something explains the empty pane", () => hintText().Alpha > 0 && hintText().Text.Contains("no-texture-is-called-this", StringComparison.Ordinal));

        AddStep("Clear the filter", () => window().SearchText.Value = string.Empty);
        AddUntilStep("Cards came back", () => countCards() > 0);
        AddAssert("The explanation went away", () => hintText().Alpha == 0);
    }

    /// <summary>
    /// The window opens in the ordering it had before there was a sort control at all: largest first,
    /// by pixel area.
    /// </summary>
    [Test]
    public void TestDefaultSortIsStillLargestFirst()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddAssert("Opens on Size", () => window().SortMode == TextureSortMode.Size);

        AddStep("Filter to the probes", () => window().SearchText.Value = "-probe");
        AddUntilStep("The filter applied", () => window().DisplayedTextures.Count == 3);

        AddAssert("Largest first", () => names().SequenceEqual(new[] { "gamma-probe", "alpha-probe", "beta-probe" }));
    }

    /// <summary>
    /// Textures the sort cannot tell apart must hold whatever order they came in, tick after tick.
    /// <see cref="TextureRegistry.GetAll"/> documents its own order as unspecified, so there is no
    /// particular order to assert — but the list settling and staying settled is exactly what the old
    /// stable <c>OrderByDescending</c> gave and what an unstable <see cref="List{T}.Sort"/> would take
    /// away, and a card grid that reshuffles ten times a second is unreadable.
    /// </summary>
    [Test]
    public void TestEqualSizesDoNotShuffleBetweenTicks()
    {
        AddStep("Add three same-sized probes", () =>
        {
            probes.Add(new Texture(new HeadlessNativeTexture(16, 16)) { Name = "tie-a" });
            probes.Add(new Texture(new HeadlessNativeTexture(16, 16)) { Name = "tie-b" });
            probes.Add(new Texture(new HeadlessNativeTexture(16, 16)) { Name = "tie-c" });
        });

        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddStep("Filter to the ties", () => window().SearchText.Value = "tie-");
        AddUntilStep("The filter applied", () => window().DisplayedTextures.Count == 3);

        string[] settled = null!;
        AddStep("Record the order", () => settled = names());

        AddWaitStep("Let many refresh ticks pass", 60);
        AddAssert("The order did not move", () => names().SequenceEqual(settled));

        AddWaitStep("And again", 60);
        AddAssert("Still where it was", () => names().SequenceEqual(settled));
    }

    [Test]
    public void TestSortCyclesAndOrders()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        // Narrowed to the probes, so the ordering assertions are against a known set rather than
        // whatever else the scene happens to have loaded.
        AddStep("Filter to the probes", () => window().SearchText.Value = "-probe");
        AddUntilStep("The filter applied", () => window().DisplayedTextures.Count == 3);

        AddAssert("Opens sorted by size, largest first", () => window().SortMode == TextureSortMode.Size
                                                              && names().SequenceEqual(new[] { "gamma-probe", "alpha-probe", "beta-probe" }));

        AddStep("Cycle to Name", () => window().CycleSortMode());
        AddUntilStep("Sorted by name", () => window().SortMode == TextureSortMode.Name
                                             && names().SequenceEqual(new[] { "alpha-probe", "beta-probe", "gamma-probe" }));

        AddStep("Cycle to Binds", () => window().CycleSortMode());
        AddAssert("Sort mode moved on", () => window().SortMode == TextureSortMode.Binds);

        // Nothing draws these, so every probe is on zero binds and the tie-break carries the order —
        // which is the property that keeps the list from shuffling every tick.
        AddUntilStep("Ties fall back to size", () => names().SequenceEqual(new[] { "gamma-probe", "alpha-probe", "beta-probe" }));

        AddStep("Cycle back round", () => window().CycleSortMode());
        AddAssert("Back to Size", () => window().SortMode == TextureSortMode.Size);
    }

    [Test]
    public void TestUnboundOnlyKeepsOnlyWhatWasNeverDrawn()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddStep("Turn on never-bound", () => window().SetUnboundOnly(true));
        AddUntilStep("The toggle applied", () => window().UnboundOnly);

        AddAssert("Nothing shown has ever been bound",
            () => window().DisplayedTextures.All(t => t.BackendTexture?.Binds?.EverBound == false));

        // Straight to the counter: the headless backend's Bind is a no-op, so there is no drawing this
        // into being bound.
        AddStep("Bind a probe", () => probes.Single(t => t.Name == "alpha-probe").BackendTexture!.Binds.Record());
        AddUntilStep("It dropped out of the list", () => window().DisplayedTextures.All(t => t.Name != "alpha-probe"));

        AddStep("Turn it off", () => window().SetUnboundOnly(false));
        AddUntilStep("It came back", () => window().DisplayedTextures.Any(t => t.Name == "alpha-probe"));
    }

    /// <summary>
    /// The point of the whole exercise: what the window builds is bounded by how big it is, not by how
    /// much the app has loaded. Before this, every texture in the app got a card — six drawables each,
    /// all live, all walked by both input queues.
    /// </summary>
    [Test]
    public void TestCardCountIsBoundedByTheViewport()
    {
        AddStep("Load a hundred textures", () =>
        {
            for (int i = 0; i < 100; i++)
                probes.Add(new Texture(new HeadlessNativeTexture(16, 16)) { Name = $"bulk-{i:000}" });
        });

        open();
        AddUntilStep("Cards were built", () => countCards() > 0);

        AddStep("Filter to the bulk", () => window().SearchText.Value = "bulk-");
        AddUntilStep("All hundred are in the list", () => window().ShownCards == 100);

        // A degenerate grid — one column, or a zero-height viewport — would satisfy "far fewer are
        // built" while being completely broken, and neither shows up in a headless render. The default
        // 900-wide window fits five 165px columns inside its padding.
        AddAssert("The grid is not degenerate", () => window().Columns >= 4);
        AddAssert("More than one row is showing", () => countCards() > window().Columns);

        // The slack row each way is what keeps this from being a tight bound worth asserting exactly.
        AddAssert("But far fewer are built", () => countCards() < 40);
        AddAssert("And the pool is no larger than what it built", () => cardPoolSize() < 40);
    }

    /// <summary>
    /// Scrolling has to reach the entries the first viewport never showed, and must not grow the pool
    /// to hold them: the same cards are re-bound as they come round.
    /// </summary>
    [Test]
    public void TestScrollingRebindsRatherThanBuilds()
    {
        AddStep("Load a hundred textures", () =>
        {
            for (int i = 0; i < 100; i++)
                probes.Add(new Texture(new HeadlessNativeTexture(16, 16)) { Name = $"bulk-{i:000}" });
        });

        open();
        AddStep("Filter to the bulk", () => window().SearchText.Value = "bulk-");
        AddUntilStep("All hundred are in the list", () => window().ShownCards == 100);

        int poolAtTop = 0;
        string[] titlesAtTop = null!;

        AddStep("Record the top of the list", () =>
        {
            poolAtTop = cardPoolSize();
            titlesAtTop = boundTitles();
        });

        AddStep("Scroll to the end", () => scroll().ScrollToEnd(false));
        AddWaitStep("Let the scroll settle", 30);

        AddAssert("Different cards are showing", () => !boundTitles().SequenceEqual(titlesAtTop));
        AddAssert("The pool did not grow", () => cardPoolSize() <= poolAtTop + 1);

        AddStep("Scroll back", () => scroll().ScrollToStart(false));
        AddWaitStep("Let the scroll settle", 30);

        AddAssert("The top of the list came back", () => boundTitles().SequenceEqual(titlesAtTop));
    }

    /// <summary>
    /// A card that scrolls out of view must let go of its texture. A pooled card is kept alive for
    /// reuse, so one still referencing a texture would keep that texture alive too — and this is the
    /// window that reports the textures nobody released.
    /// </summary>
    [Test]
    public void TestScrolledAwayCardsReleaseTheirTexture()
    {
        AddStep("Load a hundred textures", () =>
        {
            for (int i = 0; i < 100; i++)
                probes.Add(new Texture(new HeadlessNativeTexture(16, 16)) { Name = $"bulk-{i:000}" });
        });

        open();
        AddStep("Filter to the bulk", () => window().SearchText.Value = "bulk-");
        AddUntilStep("All hundred are in the list", () => window().ShownCards == 100);
        AddWaitStep("Let the grid settle", 10);

        AddAssert("Only bound cards hold a texture",
            () => collect<TextureViewerCard>(window()).All(c => c.Alpha > 0 || c.HeldTexture == null));

        AddStep("Narrow to a single card", () => window().SearchText.Value = "bulk-042");
        AddUntilStep("One entry left", () => window().ShownCards == 1);
        AddWaitStep("Let the grid settle", 10);

        AddAssert("Every unbound card let go", () => collect<TextureViewerCard>(window()).Count(c => c.HeldTexture != null) == 1);
    }

    /// <summary>
    /// Every line on a card has to read against the card. This became checkable rather than a matter of
    /// opinion once the card background stopped being half-transparent: what a label sits on is now a
    /// known colour, so the contrast can be computed instead of eyeballed.
    /// </summary>
    /// <remarks>
    /// The bug this exists for: with cards drawn in <see cref="Color.DarkGray"/> at half alpha, the
    /// "ready" state line was <see cref="Color.Gray"/> — the same value as the card — so every card had
    /// three lines of which one looked blank. Nothing headless could see it, and nothing about the code
    /// looked wrong.
    /// </remarks>
    [Test]
    public void TestEveryCardLineReadsAgainstItsCard()
    {
        open();
        AddUntilStep("Cards were built", () => countCards() > 0);
        AddWaitStep("Let the labels settle", 10);

        AddAssert("Every line on every card is legible", () =>
        {
            var cards = collect<TextureViewerCard>(window()).Where(c => c.Alpha > 0).ToArray();

            Assert.That(cards, Is.Not.Empty);

            foreach (var card in cards)
            {
                foreach (var label in card.Labels)
                {
                    Assert.That(label.Text, Is.Not.Empty, "a card line was left blank");

                    double ratio = contrastRatio(label.Color, label.Alpha, TextureViewerDisplay.CardBackground);

                    // 3:1, the floor for large or incidental text. The title and anything actually
                    // wrong clear 10:1; the quiet lines are the ones this is holding to account.
                    Assert.That(ratio, Is.GreaterThanOrEqualTo(3.0),
                        $"'{label.Text}' reads at only {ratio:0.00}:1 against the card");
                }
            }

            return true;
        });
    }

    /// <summary>
    /// WCAG contrast between a label and its background, after compositing the label's alpha onto that
    /// background — a dimmed colour is only as readable as what it actually becomes.
    /// </summary>
    private static double contrastRatio(Color color, float alpha, Color background)
    {
        double composite(int fg, int bg) => fg * alpha + bg * (1 - alpha);

        double labelLuminance = relativeLuminance(composite(color.R, background.R), composite(color.G, background.G), composite(color.B, background.B));
        double backgroundLuminance = relativeLuminance(background.R, background.G, background.B);

        double lighter = Math.Max(labelLuminance, backgroundLuminance);
        double darker = Math.Min(labelLuminance, backgroundLuminance);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double relativeLuminance(double r, double g, double b)
        => 0.2126 * linearise(r) + 0.7152 * linearise(g) + 0.0722 * linearise(b);

    private static double linearise(double channel)
    {
        double c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private ScrollableContainer scroll() => collect<ScrollableContainer>(window()).First();

    private int cardPoolSize() => collect<TextureViewerCard>(window()).Count + collect<VideoPoolInfoCard>(window()).Count;

    private string[] boundTitles() => collect<TextureViewerCard>(window()).Where(c => c.Alpha > 0).Select(c => c.BoundTitle).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    private string[] names() => window().DisplayedTextures.Select(t => t.Name).ToArray();

    /// <summary>
    /// The stand-in shown in place of the cards. The only text in the window body that is not inside
    /// the header, the toolbar or a card.
    /// </summary>
    private SpriteText hintText() => collect<SpriteText>(window()).Single(t => t.Color == Color.LightPink);

    private void open() => AddStep("Open", () => layer.Toggle(() => new TextureViewerDisplay()));

    private TextureViewerDisplay window() => layer.OpenWindows.OfType<TextureViewerDisplay>().Single();

    /// <summary>
    /// The header's own lines. Selected by their colour rather than by position, because the empty-state
    /// hint is a direct child of the body and would otherwise be counted as one of them.
    /// </summary>
    private SpriteText[] headerLines() => window().Children.OfType<Container>()
                                                 .SelectMany(c => c.Children.OfType<SpriteText>())
                                                 .Where(t => t.Color == Color.LightGreen)
                                                 .ToArray();

    /// <summary>
    /// Cards actually on screen. The pool keeps its drawables between bindings, so counting card
    /// <em>objects</em> — or the sprites inside them — counts the pool rather than the grid; a bound
    /// card is one the viewport asked for.
    /// </summary>
    private int countCards() => collect<TextureViewerCard>(window()).Count(c => c.Alpha > 0)
                                + collect<VideoPoolInfoCard>(window()).Count(c => c.Alpha > 0);

    private static List<T> collect<T>(Drawable drawable) where T : Drawable
    {
        var found = new List<T>();

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
