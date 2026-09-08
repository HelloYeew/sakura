// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Performance;

public partial class TextureViewerDisplay : DebugWindow
{
    /// <summary>
    /// How often the header, the card labels, and the card set itself are re-read.
    /// </summary>
    private const double refresh_interval = 100;

    /// <summary>
    /// Height reserved above the card list for the two header lines. Fixed, and the lines are placed at
    /// fixed offsets within it, so the two cannot disagree about where the list starts.
    /// </summary>
    private const float header_height = 50;

    private const float header_line_height = 18;

    /// <summary>
    /// Edge of one square preview card.
    /// </summary>
    private const float card_size = 160;

    private const float card_padding = 5;

    private const float card_label_spacing = 3;

    /// <summary>
    /// Space a card's flow is assumed to give one line of size-10 label text, used only to decide how
    /// much of the card is left for the preview.
    /// </summary>
    private const float card_label_height = 16;

    /// <summary>
    /// A card's content box
    /// </summary>
    private const float card_content_size = card_size - card_padding * 2;

    /// <summary>
    /// The card geometry the pooled card classes below build against. Internal rather than private
    /// because a pooled card has to outlive any one binding, so it is a separate type.
    /// </summary>
    internal const float CARD_SIZE = card_size;

    internal const float CARD_CONTENT_SIZE = card_content_size;

    internal const float CARD_PREVIEW_HEIGHT = card_preview_height;

    /// <summary>
    /// Gap between cards, on both axes.
    /// </summary>
    private const float card_spacing = 5;

    /// <summary>
    /// Distance from one card's edge to the next one's, which is what the grid counts in.
    /// </summary>
    private const float card_stride = card_size + card_spacing;

    /// <summary>
    /// Label lines above a preview: title, state, binds. Fixed rather than varying with what there is
    /// to say, because a pooled card cannot resize its preview every time it is re-bound without the
    /// grid having to re-measure. It is also why <see cref="DescribeState"/> always has an answer.
    /// </summary>
    private const int card_label_lines = 3;

    /// <summary>
    /// What is left of a card for its preview once the three label lines have had their share.
    /// </summary>
    private const float card_preview_height = card_content_size - card_label_lines * (card_label_height + card_label_spacing);

    /// <summary>
    /// Rows of cards kept bound beyond each edge of the viewport, so a row is ready before it is
    /// scrolled into view rather than appearing as it arrives.
    /// </summary>
    private const int viewport_slack_rows = 1;

    /// <summary>
    /// Height of the filter and sort row between the header and the cards.
    /// </summary>
    private const float toolbar_height = 32;

    private const float sort_button_width = 118;

    private const float unbound_button_width = 112;

    private const float toolbar_gap = 6;

    protected override string Title => "Texture & Atlas Viewer (Ctrl + F3)";
    protected override Vector2 DefaultSize => new Vector2(900, 620);

    // Wide enough for three card columns.
    protected override Vector2 MinSize => new Vector2(560, 260);

    protected override Color Accent => Color.LimeGreen;

    /// <summary>
    /// Unlike the other tools, a closed instance of this one is thrown away rather than kept since
    /// a card holds a <see cref="Texture"/> through its <see cref="Sprite"/> or an <see cref="IVideoTexture"/>,
    /// so a retained closed window keeps alive exactly the textures whose owners forgot to dispose of them, and this is the tool that
    /// reports <c>Textures / Reclaimed by GC</c>. Keeping the cards would make it hide the leaks it
    /// exists to report.
    /// </summary>
    protected internal override bool DisposeOnClose => true;

    private readonly ScrollableContainer scroll;

    /// <summary>
    /// Holds the cards at a height the grid gives it, so the scroll has an extent to work against.
    /// Absolutely positioned rather than a flow: the grid has to know which cards are on screen before
    /// it builds any of them, and a flow only knows once it has laid them all out.
    /// </summary>
    private readonly Container cardContent;

    private readonly SpriteText liveText;
    private readonly SpriteText summaryText;

    // Assigned by buildToolbar() during construction, which is why these are not readonly.
    private BasicTextBox search = null!;
    private BasicButton sortButton = null!;
    private BasicButton unboundButton = null!;

    /// <summary>
    /// Stands in for the card list when nothing survives the filter, so an empty pane reads as an
    /// answer rather than as the tool having broken.
    /// </summary>
    private readonly SpriteText emptyHint;

    /// <summary>
    /// The current filter matched case-insensitively against a card's title.
    /// </summary>
    private string filter = string.Empty;

    private TextureSortMode sortMode = TextureSortMode.Size;

    /// <summary>
    /// Whether only textures that nothing has ever drawn are shown.
    /// </summary>
    private bool unboundOnly;

    /// <summary>
    /// The standalone textures the cards should be showing, in card order, recomputed each tick.
    /// </summary>
    private readonly List<Texture> orderedStandalone = new List<Texture>();

    /// <summary>
    /// Scratch for sorting, pairing each texture with the position the texture manager enumerated it
    /// at. <see cref="List{T}.Sort(Comparison{T})"/> is not stable, so every comparison ends on that
    /// index: two textures a sort cannot tell apart hold the order they were found in rather than
    /// swapping places from one tick to the next.
    /// </summary>
    private readonly List<(Texture Texture, int Index)> sortScratch = new List<(Texture, int)>();

    /// <summary>
    /// The standalone textures the cards <em>are</em> showing, in card order. Compared against
    /// <see cref="orderedStandalone"/> so that a sort whose ordering moves — which is every tick's
    /// possibility under <see cref="TextureSortMode.Binds"/> — rebuilds, and one that did not costs a
    /// list walk.
    /// </summary>
    private readonly List<Texture> displayedStandalone = new List<Texture>();

    /// <summary>
    /// Set when the filter, the sort or the toggle changes, so the next tick rebuilds whether or not
    /// anything about the textures themselves did.
    /// </summary>
    private bool refreshQueued = true;

    /// <summary>
    /// How many cards the last rebuild produced, against how many it considered.
    /// </summary>
    private int shownCards;

    private int totalCards;

    private int lastTextureUpdates = -1;
    private int lastAtlasPageCount = -1;
    private int lastTextureAtlasPageCount = -1;
    private int lastTextureCount = -1;
    private int lastVideoCount = -1;

    private double nextRefreshTime = double.MinValue;

    private IRenderer renderer = null!;

    /// <summary>
    /// Shader for the video pool previews, shared by every preview card. Compiled once on the draw
    /// thread, written there and read on the update thread, same as <see cref="VideoSprite"/> does.
    /// </summary>
    private IShader? videoShader;

    /// <summary>
    /// Whether the compiler of <see cref="videoShader"/> has already been scheduled, so a refresh while
    /// it is still in flight does not queue another one.
    /// </summary>
    private bool videoShaderRequested;

    /// <summary>
    /// Every card the list would show, in card order, as data rather than drawables. Rebuilt on the
    /// throttled tick; the grid builds drawables only for the slice of it that is on screen.
    /// </summary>
    private readonly List<CardEntry> entries = new List<CardEntry>();

    /// <summary>
    /// Bumped whenever <see cref="entries"/> is rebuilt. A card that finds its generation and index
    /// unchanged is already showing the right thing and skips re-reading all of it, which matters
    /// because the grid re-binds every visible card every frame to follow the scroll.
    /// </summary>
    private int entryGeneration;

    /// <summary>
    /// The preview cards, reused as the grid scrolls — one per card visible at once, not one per
    /// texture in the app.
    /// </summary>
    private readonly List<TextureViewerCard> cardPool = new List<TextureViewerCard>();

    /// <summary>
    /// The pool summary cards, pooled separately because they are a different shape: five lines of
    /// figures about a pool, and no preview of their own.
    /// </summary>
    private readonly List<VideoPoolInfoCard> poolCardPool = new List<VideoPoolInfoCard>();

    /// <summary>
    /// Width and height <see cref="cardContent"/> was last laid out against, so a steady frame does not
    /// invalidate it.
    /// </summary>
    private float lastContentHeight = -1;

    private int columns = 1;

    [Resolved]
    private ITextureManager textureManager { get; set; }

    [Resolved]
    private IFontStore fontStore { get; set; }

    [Resolved]
    private AppHost host { get; set; }

    public TextureViewerDisplay()
    {
        var header = new Container
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            Height = header_height,
            Padding = new MarginPadding { Left = 10, Right = 10, Top = 8 }
        };

        header.Add(liveText = headerLine(0));
        header.Add(summaryText = headerLine(header_line_height));

        Add(header);

        scroll = new ScrollableContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        };

        scroll.Add(cardContent = new Container
        {
            RelativeSizeAxes = Axes.X,
            Width = 1f,
            Padding = new MarginPadding { Left = 10, Right = 10, Bottom = 10 },
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        Add(buildToolbar());

        var body = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            // Padding rather than a height, so the list keeps filling the window as it is resized. The
            // cards flow horizontally and wrap, so they follow the window's width on their own.
            Padding = new MarginPadding { Top = header_height + toolbar_height }
        };

        body.Add(emptyHint = new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 8),
            Font = FontUsage.Default.With(size: 12),
            Color = Color.LightPink,
            Alpha = 0,
            Text = string.Empty
        });

        body.Add(scroll);

        Add(body);
    }

    private Drawable buildToolbar()
    {
        var toolbar = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.X,
            Width = 1,
            Height = toolbar_height,
            Margin = new MarginPadding { Top = header_height },
            Padding = new MarginPadding { Left = 10, Right = 10 }
        };

        search = new BasicTextBox
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            PlaceholderText = "Filter by name, size or kind",
            BackgroundColor = Color.FromArgb(255, 22, 34, 26),
            BackgroundFocusedColor = Color.FromArgb(255, 36, 60, 42),
            // Filtering is live, so there is nothing for enter to commit.
            ReleaseFocusOnCommit = false
        };

        search.Text.ValueChanged += e =>
        {
            filter = e.NewValue ?? string.Empty;
            queueRefresh();
        };

        // The buttons are fixed width and the box takes the rest, so the filter grows with the window
        // and the controls stay where they were.
        toolbar.Add(new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding
            {
                Right = sort_button_width + unbound_button_width + toolbar_gap * 2,
                Top = 2,
                Bottom = 4
            },
            Child = search
        });

        toolbar.Add(sortButton = new BasicButton
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-(unbound_button_width + toolbar_gap), 2),
            Size = new Vector2(sort_button_width, toolbar_height - 6),
            TextSize = 12,
            DefaultColor = Color.DarkGreen,
            HoverColor = Color.Green,
            Action = cycleSortMode
        });

        toolbar.Add(unboundButton = new BasicButton
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(0, 2),
            Size = new Vector2(unbound_button_width, toolbar_height - 6),
            Text = "Never bound",
            TextSize = 12,
            Action = () =>
            {
                unboundOnly = !unboundOnly;
                updateToolbarLabels();
                queueRefresh();
            }
        });

        updateToolbarLabels();

        return toolbar;
    }

    private void cycleSortMode()
    {
        sortMode = sortMode switch
        {
            TextureSortMode.Size => TextureSortMode.Name,
            TextureSortMode.Name => TextureSortMode.Binds,
            _ => TextureSortMode.Size
        };

        updateToolbarLabels();
        queueRefresh();
    }

    private void updateToolbarLabels()
    {
        sortButton.Text = $"Sort: {sortMode}";

        unboundButton.DefaultColor = unboundOnly ? Color.DarkRed : Color.DimGray;
        unboundButton.HoverColor = unboundOnly ? Color.Red : Color.Gray;
    }

    /// <summary>
    /// Rebuilds on the next frame rather than up to <see cref="refresh_interval"/> later, since every
    /// caller is answering a keystroke or a click.
    /// </summary>
    private void queueRefresh()
    {
        refreshQueued = true;
        nextRefreshTime = double.MinValue;
    }

    private static SpriteText headerLine(float y) => new SpriteText
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Font = FontUsage.Default.With(size: 14),
        Color = Color.LightGreen,
        Position = new Vector2(0, y),
        Height = header_line_height
    };

    /// <summary>
    /// The filter box. Matched case-insensitively against each card's title.
    /// </summary>
    public Reactive<string> SearchText => search.Text;

    /// <summary>
    /// How the standalone textures are ordered. Cycled by the toolbar button.
    /// </summary>
    public TextureSortMode SortMode => sortMode;

    /// <summary>
    /// Whether the list is narrowed to textures nothing has ever drawn.
    /// </summary>
    public bool UnboundOnly => unboundOnly;

    /// <summary>
    /// How many cards the list is showing, and how many it would show unfiltered.
    /// </summary>
    public int ShownCards => shownCards;

    public int TotalCards => totalCards;

    /// <summary>
    /// How many cards the grid fits across at the window's current width.
    /// </summary>
    public int Columns => columns;

    /// <summary>
    /// The standalone textures on screen, in card order.
    /// </summary>
    public IReadOnlyList<Texture> DisplayedTextures => displayedStandalone;

    /// <summary>
    /// Steps the sort on, exactly as the toolbar button does.
    /// </summary>
    public void CycleSortMode() => cycleSortMode();

    /// <summary>
    /// Turns the never-bound filter on or off, exactly as the toolbar button does.
    /// </summary>
    public void SetUnboundOnly(bool value)
    {
        if (unboundOnly == value)
            return;

        unboundOnly = value;
        updateToolbarLabels();
        queueRefresh();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        renderer = host.Renderer;
    }

    public override void Update()
    {
        base.Update();

        if (Clock.CurrentTime >= nextRefreshTime)
        {
            nextRefreshTime = Clock.CurrentTime + refresh_interval;
            refreshTick();
        }

        // Every frame, not on the tick: this follows the scroll, and a grid that only caught up ten
        // times a second would tear away from the scrollbar.
        updateViewport();
    }

    /// <summary>
    /// The throttled half: what the card list should contain, and everything on screen that is read
    /// rather than positioned.
    /// </summary>
    private void refreshTick()
    {
        int currentTextureUpdates = GlobalStatistics.Get<int>("Textures", "Texture Updates", StatisticKind.Cumulative).Value;
        int currentAtlasPageCount = fontStore.Atlas != null ? fontStore.Atlas.GetAllPages().Count() : 0;
        int currentTextureAtlasPageCount = textureManager.Atlas?.PageCount ?? 0;
        int currentTextureCount = textureManager.GetAllTextures().Count();
        int currentVideoCount = textureManager.GetAllVideoTextures().Count();

        bool texturesChanged = currentTextureUpdates != lastTextureUpdates || currentAtlasPageCount != lastAtlasPageCount || currentTextureAtlasPageCount != lastTextureAtlasPageCount
                               || currentTextureCount != lastTextureCount || currentVideoCount != lastVideoCount;

        lastTextureUpdates = currentTextureUpdates;
        lastAtlasPageCount = currentAtlasPageCount;
        lastTextureAtlasPageCount = currentTextureAtlasPageCount;
        lastTextureCount = currentTextureCount;
        lastVideoCount = currentVideoCount;

        // Recomputed unconditionally, because none of the counters above move when a sort's ordering
        // does. Under Binds that is a live figure and the order can change from one tick to the next;
        // under Size and Name it cannot change without the texture set changing too, so the walk finds
        // no difference and nothing is rebuilt.
        buildStandaloneOrder();

        if (texturesChanged || refreshQueued || !orderedStandalone.SequenceEqual(displayedStandalone))
            rebuildEntries();

        // Only the cards on screen, and only ten times a second. A bind count and a video's upload
        // state both change faster than anyone can read them, and nothing off screen is being read at
        // all.
        foreach (var card in cardPool)
            card.RefreshLiveLabels();

        foreach (var card in poolCardPool)
            card.RefreshLiveLabels();

        updateHeader();
    }

    private void updateHeader()
    {
        long liveBytes = TextureRegistry.LiveBytes;
        long peakBytes = GlobalStatistics.Get<long>("Textures", "Peak Bytes", StatisticKind.Gauge, StatisticUnit.Bytes).Value;
        long reclaimed = GlobalStatistics.Get<long>("Textures", "Reclaimed by GC", StatisticKind.Cumulative).Value;

        int slices = TextureRegistry.LiveSliceCount;

        liveText.Text = $"Live: {TextureRegistry.LiveCount} textures, {toMegabytes(liveBytes)} (peak {toMegabytes(peakBytes)})"
                        + (slices > 0 ? $" + {slices} atlas slices" : "")
                        // Only when something is actually being held back: an unfiltered list showing
                        // all of itself does not need to say so.
                        + (shownCards < totalCards ? $" — showing {shownCards} of {totalCards} cards" : "")
                        + (reclaimed > 0 ? $" — {reclaimed} reclaimed by GC (a Dispose is being missed!)" : "");

        int textureBinds = GlobalStatistics.Get<int>("Renderer", "Texture Binds This Frame", StatisticKind.PerFrame).Value;

        summaryText.Text = $"Binds last frame: {textureBinds}"
                           + $"   Native: {toMegabytes(NativeMemoryTracker.TotalBytes)} (peak {toMegabytes(NativeMemoryTracker.PeakTotalBytes)})";
    }

    internal static string ToMegabytes(long bytes) => $"{bytes / 1024.0 / 1024.0:0.0} MB";

    private static string toMegabytes(long bytes) => ToMegabytes(bytes);

    /// <summary>
    /// The label-and-preview column inside a card.
    /// </summary>
    internal static FlowContainer CardFlow(Drawable[] children) => new FlowContainer
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Direction = FlowDirection.Vertical,
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Width = 1,
        Spacing = new Vector2(0, card_label_spacing),
        Padding = new MarginPadding(card_padding),
        Children = children
    };

    /// <summary>
    /// One label line of a card. A truncated title ends in an ellipsis rather than the mid-glyph cut
    /// the card's own masking would give it — most texture names are full asset paths, and wider than
    /// a card.
    /// </summary>
    internal static SpriteText CardLabel(Color color, bool truncate = false) => new SpriteText
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Font = FontUsage.Default.With(size: 10),
        Color = color,
        Truncate = truncate,
        MaxWidth = truncate ? card_content_size : float.MaxValue,
        Text = string.Empty
    };

    /// <summary>
    /// Compiles the preview shader the first time there is a video pool to preview, so an app that never
    /// plays a video doesn't need to compile it.
    /// </summary>
    private void ensureVideoShader()
    {
        if (videoShader != null || videoShaderRequested)
            return;

        videoShaderRequested = true;
        renderer.ScheduleToDrawThread(() => videoShader = VideoTexturePreview.CreateShader(renderer));
    }

    /// <summary>
    /// Recomputes which standalone textures belong on screen and in what order. Kept apart from
    /// <see cref="rebuildEntries"/> so the answer can be compared against what is on screen without
    /// building anything.
    /// </summary>
    private void buildStandaloneOrder()
    {
        orderedStandalone.Clear();
        sortScratch.Clear();

        var fontAtlas = fontStore.Atlas;

        // This runs every tick, and describe() formats a string per texture. With nothing to filter on
        // there is no question for it to answer, so it is not asked.
        bool filtering = filter.Length > 0 || unboundOnly;

        foreach (var texture in textureManager.GetAllTextures())
        {
            // A font atlas page is listed once, under its own heading at the bottom, rather than again
            // here for every texture that is a slice of it.
            if (texture == null || (fontAtlas?.OwnsNativeTexture(texture.BackendTexture) ?? false))
                continue;

            if (filtering && !include(describe(texture), texture.BackendTexture?.Binds))
                continue;

            sortScratch.Add((texture, sortScratch.Count));
        }

        // Only the standalone textures are sorted. A video pool's summary card has to stay in front of
        // the textures it describes, and an atlas page number only means anything in page order, so
        // both keep the order they are enumerated in.
        switch (sortMode)
        {
            case TextureSortMode.Name:
                sortScratch.Sort(static (a, b) =>
                {
                    int byName = string.Compare(displayName(a.Texture), displayName(b.Texture), StringComparison.OrdinalIgnoreCase);
                    return byName != 0 ? byName : a.Index.CompareTo(b.Index);
                });
                break;

            case TextureSortMode.Binds:
                // Area breaks the tie first, so the many textures sitting at zero binds are still
                // ordered by something a reader can see rather than by enumeration order alone.
                sortScratch.Sort(static (a, b) =>
                {
                    int byBinds = bindsOf(b.Texture).CompareTo(bindsOf(a.Texture));

                    if (byBinds != 0)
                        return byBinds;

                    int byArea = areaOf(b.Texture).CompareTo(areaOf(a.Texture));
                    return byArea != 0 ? byArea : a.Index.CompareTo(b.Index);
                });
                break;

            default:
                sortScratch.Sort(static (a, b) =>
                {
                    int byArea = areaOf(b.Texture).CompareTo(areaOf(a.Texture));
                    return byArea != 0 ? byArea : a.Index.CompareTo(b.Index);
                });
                break;
        }

        foreach (var (texture, _) in sortScratch)
            orderedStandalone.Add(texture);
    }

    private static long areaOf(Texture texture) => (long)texture.Width * texture.Height;

    private static int bindsOf(Texture texture) => texture.BackendTexture?.Binds?.LastFrame ?? 0;

    /// <summary>
    /// Whether a card belongs on screen under the current filter and toggle.
    /// </summary>
    /// <param name="title">The card's title, which is what the filter is matched against — so it
    /// covers a texture's name, its dimensions, and the kind of thing it is in one box.</param>
    /// <param name="counter">The card's bind counter, or null where there is nothing to count against.</param>
    private bool include(string title, TextureBindCounter? counter)
    {
        // Something with no counter has no answer to "has this ever been drawn", so it is not one of
        // the things this toggle is asking about.
        if (unboundOnly && (counter == null || counter.EverBound))
            return false;

        return filter.Length == 0 || title.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rebuilds the list of what the grid should show. Produces no drawables: the cards for the slice
    /// of it that is on screen are bound by <see cref="updateViewport"/>.
    /// </summary>
    private void rebuildEntries()
    {
        entries.Clear();
        refreshQueued = false;

        totalCards = 0;

        var fontAtlas = fontStore.Atlas;

        // Filtering and ordering already happened in buildStandaloneOrder, which runs every tick; this
        // only has to count what it left out.
        totalCards += textureManager.GetAllTextures().Count(t => t != null && !(fontAtlas?.OwnsNativeTexture(t.BackendTexture) ?? false));

        displayedStandalone.Clear();

        foreach (var tex in orderedStandalone)
        {
            entries.Add(CardEntry.ForTexture(describe(tex), tex));
            displayedStandalone.Add(tex);
        }

        var videoTextures = textureManager.GetAllVideoTextures()
            .Where(vt => vt != null)
            .ToList();

        if (videoTextures.Count > 0)
        {
            ensureVideoShader();

            var groups = videoTextures
                .GroupBy(vt => (vt.Width, vt.Height));

            foreach (var group in groups)
            {
                var pooled = group.ToList();

                string poolTitle = $"Video Texture Pool {group.Key.Width}x{group.Key.Height}";
                totalCards++;

                if (include(poolTitle, null))
                    entries.Add(CardEntry.ForVideoPool(poolTitle, group.Key.Width, group.Key.Height, pooled));

                for (int i = 0; i < pooled.Count; i++)
                {
                    totalCards++;

                    string title = videoCardTitle(pooled[i], i, pooled.Count);

                    if (include(title, pooled[i].Binds))
                        entries.Add(CardEntry.ForVideoTexture(title, pooled[i]));
                }
            }
        }

        if (textureManager.Atlas != null)
        {
            int texturePageIndex = 0;

            foreach (var atlasPage in textureManager.Atlas.GetAllPages())
            {
                string title = $"Texture Atlas Page {texturePageIndex} ({atlasPage.Width}x{atlasPage.Height})";
                texturePageIndex++;
                totalCards++;

                if (include(title, atlasPage.BackendTexture?.Binds))
                    entries.Add(CardEntry.ForTexture(title, atlasPage));
            }
        }

        if (fontAtlas != null)
        {
            int pageIndex = 0;

            foreach (var atlasPage in fontAtlas.GetAllPages())
            {
                string title = $"Font Atlas Page {pageIndex} ({atlasPage.Width}x{atlasPage.Height})";
                pageIndex++;
                totalCards++;

                if (include(title, atlasPage.BackendTexture?.Binds))
                    entries.Add(CardEntry.ForTexture(title, atlasPage));
            }
        }

        shownCards = entries.Count;
        entryGeneration++;

        emptyHint.Text = emptyMessage();
        emptyHint.Alpha = shownCards == 0 ? 1 : 0;
    }

    /// <summary>
    /// Lays the grid out and binds a card to each entry the viewport can reach, growing the pools to
    /// whatever that slice needs and no further.
    /// </summary>
    private void updateViewport()
    {
        var viewport = scroll.ChildSize;

        // Padding is on cardContent, so the space cards actually get is narrower than the viewport.
        float usable = viewport.X - cardContent.Padding.Total.X;

        // One column always, even in a window too narrow for a whole card: a clipped card beats none.
        columns = Math.Max(1, (int)((usable + card_spacing) / card_stride));

        int rows = (entries.Count + columns - 1) / columns;
        float contentHeight = rows * card_stride;

        if (!Precision.AlmostEquals(contentHeight, lastContentHeight))
        {
            cardContent.Height = contentHeight;
            lastContentHeight = contentHeight;
        }

        float scrollY = scroll.CurrentScroll.Y;

        int firstRow = Math.Max(0, (int)(scrollY / card_stride) - viewport_slack_rows);
        int lastRow = Math.Min(rows - 1, (int)((scrollY + viewport.Y) / card_stride) + viewport_slack_rows);

        int first = firstRow * columns;
        int last = Math.Min(entries.Count - 1, (lastRow + 1) * columns - 1);

        int usedCards = 0;
        int usedPoolCards = 0;

        for (int index = first; index <= last; index++)
        {
            var entry = entries[index];
            float x = index % columns * card_stride;
            float y = index / columns * card_stride;

            if (entry.Kind == CardKind.VideoPool)
            {
                var card = takePoolCard(usedPoolCards++);
                card.Bind(entry, entryGeneration, index);
                card.Position = new Vector2(x, y);
                card.Alpha = 1;
            }
            else
            {
                var card = takeCard(usedCards++);
                card.Bind(entry, entryGeneration, index, videoShader);
                card.Position = new Vector2(x, y);
                card.Alpha = 1;
            }
        }

        // Hidden rather than removed: a hidden subtree is skipped by both input queues and costs a
        // container's worth of traversal, and the grid is about to need it again. Unbinding matters
        // more than hiding does — a card still holding a Texture keeps it alive, and this is the window
        // that reports textures nobody released.
        for (int i = usedCards; i < cardPool.Count; i++)
            cardPool[i].Unbind();

        for (int i = usedPoolCards; i < poolCardPool.Count; i++)
            poolCardPool[i].Unbind();
    }

    private TextureViewerCard takeCard(int index)
    {
        while (cardPool.Count <= index)
        {
            var card = new TextureViewerCard();
            cardPool.Add(card);
            cardContent.Add(card);
        }

        return cardPool[index];
    }

    private VideoPoolInfoCard takePoolCard(int index)
    {
        while (poolCardPool.Count <= index)
        {
            var card = new VideoPoolInfoCard();
            poolCardPool.Add(card);
            cardContent.Add(card);
        }

        return poolCardPool[index];
    }

    /// <summary>
    /// What to say in place of the cards. An empty list is either an app with no textures or a filter
    /// that matched none of them, and the two want different things done about them.
    /// </summary>
    private string emptyMessage()
    {
        if (totalCards == 0)
            return "Nothing loaded yet.";

        if (unboundOnly && filter.Length > 0)
            return $"None of the {totalCards} textures are both unbound and match \"{filter}\".";

        if (unboundOnly)
            return $"Everything loaded has been drawn at least once ({totalCards} textures).";

        return $"No match for \"{filter}\" among {totalCards} textures.";
    }

    /// <summary>
    /// A card label for a standalone texture
    /// </summary>
    private static string describe(Texture texture)
    {
        long bytes = areaOf(texture) * 4;
        string size = $"{texture.Width}x{texture.Height}, {toMegabytes(bytes)}";

        return $"{displayName(texture)} ({size})";
    }

    /// <summary>
    /// What a texture is called, for the name sort and for the front of its card label. An unnamed
    /// texture is one created from pixel data without a cache key, so there is nothing better to
    /// call it.
    /// </summary>
    private static string displayName(Texture texture) => string.IsNullOrEmpty(texture.Name) ? "Texture" : texture.Name;

    /// <summary>
    /// A card label for one pooled video texture. Composed here rather than inline, so the filter is
    /// matched against exactly the string the card ends up showing.
    /// </summary>
    private static string videoCardTitle(IVideoTexture videoTexture, int index, int total)
        => $"Video Pool Texture {index + 1}/{total} ({videoTexture.Width}x{videoTexture.Height}, {toMegabytes(NativeTextureMemory.BytesForVideoPlanes(videoTexture.Width, videoTexture.Height))})";

    /// <summary>
    /// A card's background. Opaque and dark, so that what a label contrasts against is a known colour
    /// rather than whatever the app happens to be drawing behind a half-transparent window — which is
    /// what made a gray line on a card invisible in the first place.
    /// </summary>
    /// <remarks>
    /// Faintly green rather than neutral, to sit with the window's <see cref="Accent"/> and the filter
    /// box, and light enough against the window body that a card still reads as a panel on top of it.
    /// </remarks>
    internal static Color CardBackground => Color.FromArgb(255, 30, 36, 32);

    /// <summary>
    /// A pool summary card, kept a step darker than a preview card, so the two are still told apart at a
    /// glance now that neither is gray.
    /// </summary>
    internal static Color PoolCardBackground => Color.FromArgb(255, 18, 23, 20);

    /// <summary>
    /// How a quiet card line is dimmed. Everything a card says is said in a light color, and the lines
    /// that are merely reassuring step back by alpha — never by moving their color toward the
    /// background, which is the mistake that made "ready" invisible when cards were light.
    /// </summary>
    internal const float CARD_QUIET_ALPHA = 0.6f;

    /// <summary>
    /// For a line reporting that there is nothing to report. The faintest a card line is allowed to
    /// get, and still above 3:1 against <see cref="CardBackground"/>.
    /// </summary>
    internal const float CARD_ABSENT_ALPHA = 0.5f;

    /// <summary>
    /// The state line of a texture's card. Every card has one, whether there is anything wrong,
    /// so that a pooled card's preview can be a fixed size — and because "ready" is itself an answer
    /// when the question is why something is not drawing.
    /// </summary>
    internal static (string Text, Color Color, float Alpha) DescribeState(Texture texture)
    {
        var backend = texture.BackendTexture;

        if (backend == null)
            return ("proxy (no GPU texture)", Color.LightGray, 1);

        if (backend.Handle == IntPtr.Zero)
            return ("GPU texture destroyed", Color.Red, 1);

        if (!backend.Available)
            return ("uploading", Color.Yellow, 1);

        return ("ready", Color.LightGray, CARD_QUIET_ALPHA);
    }

    /// <summary>
    /// A state line for one pooled video texture, describing whether its preview can show anything.
    /// </summary>
    internal static (string Text, Color Color, float Alpha) DescribeVideoState(IVideoTexture videoTexture, IShader? shader)
    {
        if (shader == null)
            return ("compiling preview shader", Color.LightGray, 1);

        if (videoTexture.IsDisposed)
            return ("disposed", Color.Red, 1);

        // A pooled texture between frames keeps the last frame uploaded into it, so this is only really
        // seen before the first frame of a video arrives.
        if (!videoTexture.UploadComplete)
            return ("awaiting upload", Color.Yellow, 1);

        return ("holding a frame", Color.LimeGreen, 1);
    }

    /// <summary>
    /// The bind line of a card, for a counter that may not exist — an atlas slice and a dimension-only
    /// proxy have nothing to count against.
    /// </summary>
    internal static (string Text, Color Color, float Alpha) DescribeBinds(TextureBindCounter? counter)
    {
        if (counter == null)
            return ("no bind counter", Color.LightGray, CARD_ABSENT_ALPHA);

        int binds = counter.LastFrame;

        return ($"{binds} bind{(binds == 1 ? "" : "s")} last frame", Color.LightGray, binds > 0 ? 1 : CARD_QUIET_ALPHA);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (IsDisposed) return;

        if (videoShader != null)
        {
            var shader = videoShader;
            videoShader = null;
            renderer?.ScheduleToDrawThread(shader.Dispose);
        }

        base.Dispose(isDisposing);
    }
}

internal enum CardKind
{
    Texture,
    VideoTexture,
    VideoPool
}

/// <summary>
/// One card in <see cref="TextureViewerDisplay"/>'s grid, as data. The list is built out of these and
/// only the slice of it the viewport can reach is ever turned into drawables.
/// </summary>
internal readonly struct CardEntry
{
    public readonly CardKind Kind;
    public readonly string Title;
    public readonly Texture? Texture;
    public readonly IVideoTexture? VideoTexture;
    public readonly List<IVideoTexture>? Pool;
    public readonly int PoolWidth;
    public readonly int PoolHeight;

    private CardEntry(CardKind kind, string title, Texture? texture, IVideoTexture? videoTexture, List<IVideoTexture>? pool, int poolWidth, int poolHeight)
    {
        Kind = kind;
        Title = title;
        Texture = texture;
        VideoTexture = videoTexture;
        Pool = pool;
        PoolWidth = poolWidth;
        PoolHeight = poolHeight;
    }

    public static CardEntry ForTexture(string title, Texture texture) => new CardEntry(CardKind.Texture, title, texture, null, null, 0, 0);

    public static CardEntry ForVideoTexture(string title, IVideoTexture videoTexture) => new CardEntry(CardKind.VideoTexture, title, null, videoTexture, null, 0, 0);

    public static CardEntry ForVideoPool(string title, int width, int height, List<IVideoTexture> pool) => new CardEntry(CardKind.VideoPool, title, null, null, pool, width, height);
}

/// <summary>
/// One preview card, pooled and re-bound as <see cref="TextureViewerDisplay"/>'s grid scrolls, so
/// these exist per card on screen rather than per texture in the app.
/// </summary>
/// <remarks>
/// Shows a <see cref="CardKind.Texture"/> or a <see cref="CardKind.VideoTexture"/>: the two have the
/// same three label lines and differ only in what draws the preview, so one class covers both and the
/// video preview is only built if a video is ever bound to it.
/// </remarks>
internal partial class TextureViewerCard : Container
{
    private readonly SpriteText titleText;
    private readonly SpriteText stateText;
    private readonly SpriteText bindText;
    private readonly Container previewArea;
    private readonly Sprite sprite;

    /// <summary>
    /// Built on the first video bound to this card, so a card that only ever shows textures never pays
    /// for one.
    /// </summary>
    private VideoTexturePreview? videoPreview;

    private CardEntry entry;
    private IShader? shader;
    private int boundGeneration = -1;
    private int boundIndex = -1;

    /// <summary>
    /// The texture this card is holding a reference to, or null once it has let go. Exists so a test
    /// can score the letting-go, which is the part that is invisible and the part that matters.
    /// </summary>
    internal Texture? HeldTexture => sprite.Texture;

    /// <summary>
    /// The title this card is currently showing.
    /// </summary>
    internal string BoundTitle => titleText.Text;

    /// <summary>
    /// The card's three label lines, for tests that care about what a card says rather than what it is
    /// showing.
    /// </summary>
    internal IEnumerable<SpriteText> Labels
    {
        get
        {
            yield return titleText;
            yield return stateText;
            yield return bindText;
        }
    }

    public TextureViewerCard()
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        Size = new Vector2(TextureViewerDisplay.CARD_SIZE);

        // A card clips its own content: a preview or a label that misjudges the space left over stays
        // inside its card rather than drawing across the one next to it.
        Masking = true;
        Alpha = 0;

        Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = TextureViewerDisplay.CardBackground
        });

        Add(TextureViewerDisplay.CardFlow(new Drawable[]
        {
            titleText = TextureViewerDisplay.CardLabel(Color.White, truncate: true),
            stateText = TextureViewerDisplay.CardLabel(Color.LightGray),
            bindText = TextureViewerDisplay.CardLabel(Color.LightGray),
            previewArea = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Size = new Vector2(TextureViewerDisplay.CARD_CONTENT_SIZE, TextureViewerDisplay.CARD_PREVIEW_HEIGHT),
                Child = sprite = new Sprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(1),
                    RelativeSizeAxes = Axes.Both,
                    FillMode = TextureFillMode.Fit
                }
            }
        }));
    }

    /// <summary>
    /// Points this card at an entry. Cheap to call every frame: an entry it is already showing only
    /// costs the two integer comparisons, which is what makes following the scroll affordable.
    /// </summary>
    public void Bind(CardEntry newEntry, int generation, int index, IShader? videoShader)
    {
        bool sameEntry = generation == boundGeneration && index == boundIndex;

        // The shader arrives from the draw thread some frames after the window opens, and a video
        // preview built without one cannot draw. Catching it here is what removes the forced full
        // rebuild the old code needed for it.
        bool sameShader = ReferenceEquals(videoShader, shader);

        if (sameEntry && sameShader)
            return;

        entry = newEntry;
        shader = videoShader;
        boundGeneration = generation;
        boundIndex = index;

        titleText.Text = entry.Title;

        if (entry.Kind == CardKind.VideoTexture && entry.VideoTexture != null)
        {
            sprite.Texture = null;
            sprite.Alpha = 0;

            if (videoPreview == null)
            {
                previewArea.Add(videoPreview = new VideoTexturePreview(null, null)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both
                });
            }

            videoPreview.Bind(entry.VideoTexture, videoShader);
            videoPreview.Size = previewScale(entry.VideoTexture);
            videoPreview.Alpha = 1;
        }
        else
        {
            if (videoPreview != null)
            {
                videoPreview.Bind(null, null);
                videoPreview.Alpha = 0;
            }

            sprite.Texture = entry.Texture;
            sprite.Alpha = 1;
        }

        RefreshLiveLabels();
    }

    /// <summary>
    /// Re-reads the two lines that move on their own — bind counts change every frame, and a playing
    /// video flips its upload state several times a second.
    /// </summary>
    public void RefreshLiveLabels()
    {
        if (boundIndex < 0)
            return;

        var state = entry.Kind == CardKind.VideoTexture && entry.VideoTexture != null
            ? TextureViewerDisplay.DescribeVideoState(entry.VideoTexture, shader)
            : TextureViewerDisplay.DescribeState(entry.Texture!);

        stateText.Text = state.Text;
        stateText.Color = state.Color;
        stateText.Alpha = state.Alpha;

        var binds = TextureViewerDisplay.DescribeBinds(entry.Kind == CardKind.VideoTexture ? entry.VideoTexture?.Binds : entry.Texture?.BackendTexture?.Binds);

        bindText.Text = binds.Text;
        bindText.Color = binds.Color;
        bindText.Alpha = binds.Alpha;
    }

    /// <summary>
    /// Hides this card and drops what it was showing. The dropping is the part that matters: a card
    /// still referencing a texture keeps that texture alive, and this window's whole job includes
    /// reporting the textures nobody released.
    /// </summary>
    public void Unbind()
    {
        Alpha = 0;
        boundGeneration = -1;
        boundIndex = -1;
        entry = default;
        shader = null;

        sprite.Texture = null;
        videoPreview?.Bind(null, null);
    }

    /// <summary>
    /// A video frame lives in YUV planes rather than a Texture, so FillMode has no aspect ratio to
    /// letterbox against — the preview is scaled to the frame's aspect within the card by hand.
    /// </summary>
    private static Vector2 previewScale(IVideoTexture videoTexture)
    {
        float areaAspect = TextureViewerDisplay.CARD_CONTENT_SIZE / TextureViewerDisplay.CARD_PREVIEW_HEIGHT;
        float frameAspect = videoTexture.Height > 0 ? (float)videoTexture.Width / videoTexture.Height : areaAspect;

        return frameAspect > areaAspect
            ? new Vector2(1, areaAspect / frameAspect)
            : new Vector2(frameAspect / areaAspect, 1);
    }
}

/// <summary>
/// The summary card in front of a video texture pool: five lines of figures about the pool, and no
/// preview of its own. Pooled separately from <see cref="TextureViewerCard"/> because that shape has
/// nothing in common with a preview card beyond the background behind it.
/// </summary>
internal partial class VideoPoolInfoCard : Container
{
    private readonly SpriteText dimensionsText;
    private readonly SpriteText eachText;
    private readonly SpriteText poolText;
    private readonly SpriteText uploadedText;

    private List<IVideoTexture>? pool;
    private int boundGeneration = -1;
    private int boundIndex = -1;

    public VideoPoolInfoCard()
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        Size = new Vector2(TextureViewerDisplay.CARD_SIZE);
        Masking = true;
        Alpha = 0;

        Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = TextureViewerDisplay.PoolCardBackground
        });

        Add(TextureViewerDisplay.CardFlow(new Drawable[]
        {
            new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = "Video Texture Pool",
                Font = FontUsage.Default.With(size: 12, weight: "Bold"),
                Color = Color.White
            },
            dimensionsText = TextureViewerDisplay.CardLabel(Color.LightGray),
            eachText = TextureViewerDisplay.CardLabel(Color.LightGray),
            poolText = TextureViewerDisplay.CardLabel(Color.LightGray),
            uploadedText = TextureViewerDisplay.CardLabel(Color.LightGray)
        }));
    }

    public void Bind(CardEntry entry, int generation, int index)
    {
        if (generation == boundGeneration && index == boundIndex)
            return;

        boundGeneration = generation;
        boundIndex = index;
        pool = entry.Pool;

        // The YUV planes, not width * height * 4: a video texture is three single-channel planes, so it
        // costs 1.5 bytes per pixel. The same figure the "video" total in the native memory line is
        // built from, so the two agree.
        long bytes = NativeTextureMemory.BytesForVideoPlanes(entry.PoolWidth, entry.PoolHeight);
        int total = pool?.Count ?? 0;

        dimensionsText.Text = $"{entry.PoolWidth}x{entry.PoolHeight},";
        eachText.Text = $"{TextureViewerDisplay.ToMegabytes(bytes)} each";
        poolText.Text = $"Pool: {total} ({TextureViewerDisplay.ToMegabytes(bytes * total)})";

        RefreshLiveLabels();
    }

    public void RefreshLiveLabels()
    {
        if (pool == null)
            return;

        int uploaded = pool.Count(vt => vt.UploadComplete);

        uploadedText.Text = $"Uploaded: {uploaded} / {pool.Count}";
        uploadedText.Color = uploaded == pool.Count ? Color.LimeGreen : Color.Yellow;
    }

    public void Unbind()
    {
        Alpha = 0;
        boundGeneration = -1;
        boundIndex = -1;

        // A pool card holds the list the manager handed out, which holds every texture in the pool.
        pool = null;
    }
}

/// <summary>
/// How <see cref="TextureViewerDisplay"/> orders the standalone textures in its card list.
/// </summary>
public enum TextureSortMode
{
    /// <summary>
    /// Largest first, by pixel area. What the tool is opened with, since the question behind it is
    /// usually "what is costing the most".
    /// </summary>
    Size,

    /// <summary>
    /// Alphabetical by name, for finding one texture you already know the name of.
    /// </summary>
    Name,

    /// <summary>
    /// Most-bound first, over the last completed frame, so what the renderer is actually working
    /// hardest on comes to the top. Ties break in size.
    /// </summary>
    Binds
}
