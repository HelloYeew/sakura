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
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.IO;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Performance;

public partial class TextureViewerDisplay : DebugWindow
{
    /// <summary>
    /// How often the header, the card labels, and the card set itself are re-read.
    /// </summary>
    private const double refresh_interval = 100;

    /// <summary>
    /// Height reserved above the card list for the three header lines. Fixed, and the lines are
    /// placed at fixed offsets within it, so the two cannot disagree about where the list starts.
    /// </summary>
    private const float header_height = 68;

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

    private readonly FlowContainer flowContainer;
    private readonly SpriteText liveText;
    private readonly SpriteText summaryText;
    private readonly SpriteText categoryText;

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
    /// Whether the cards currently on screen were built with <see cref="videoShader"/> available.
    /// Cards created before the compiler finished cannot draw a frame, so a refresh is forced once it is.
    /// </summary>
    private bool builtWithVideoShader;

    /// <summary>
    /// The upload-state labels of the live video pool cards, kept up to date on the throttled tick.
    /// Everything else about a card is fixed when it is built. However, a playing video flips these several
    /// times a second — a label that only changed when the pool's size did would sit there
    /// contradicting the preview right next to it.
    /// </summary>
    private readonly List<(SpriteText Label, IVideoTexture Texture)> videoStateLabels = new List<(SpriteText, IVideoTexture)>();

    /// <summary>
    /// The "Uploaded: n / total" label of each video pool summary card, kept up to date alongside
    /// <see cref="videoStateLabels"/>.
    /// </summary>
    private readonly List<(SpriteText Label, List<IVideoTexture> Pool)> videoPoolLabels = new List<(SpriteText, List<IVideoTexture>)>();

    /// <summary>
    /// The per-frame bind label of every card, refreshed on the throttled tick like the video state
    /// labels. A bind count changes every frame, so these can never be baked in at card-build time.
    /// </summary>
    private readonly List<(SpriteText Label, TextureBindCounter Counter)> bindLabels = new List<(SpriteText, TextureBindCounter)>();

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
        header.Add(categoryText = headerLine(header_line_height * 2));

        Add(header);

        var scrollContainer = new ScrollableContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        };

        scrollContainer.Add(flowContainer = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            Width = 1f,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(5),
            Padding = new MarginPadding { Left = 10, Right = 10, Bottom = 10 },
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        // Padding rather than a height, so the list keeps filling the window as it is resized. The
        // cards flow horizontally and wrap, so they follow the window's width on their own.
        Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding { Top = header_height },
            Child = scrollContainer
        });
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

    protected override void LoadComplete()
    {
        base.LoadComplete();

        renderer = host.Renderer;
    }

    public override void Update()
    {
        base.Update();

        if (Clock.CurrentTime < nextRefreshTime)
            return;

        nextRefreshTime = Clock.CurrentTime + refresh_interval;

        updateHeader();
        updateVideoLabels();
        updateBindLabels();

        int currentTextureUpdates = GlobalStatistics.Get<int>("Textures", "Texture Updates", StatisticKind.Cumulative).Value;
        int currentAtlasPageCount = fontStore.Atlas != null ? fontStore.Atlas.GetAllPages().Count() : 0;
        int currentTextureAtlasPageCount = textureManager.Atlas?.PageCount ?? 0;
        int currentTextureCount = textureManager.GetAllTextures().Count();
        int currentVideoCount = textureManager.GetAllVideoTextures().Count();

        if (currentTextureUpdates != lastTextureUpdates || currentAtlasPageCount != lastAtlasPageCount || currentTextureAtlasPageCount != lastTextureAtlasPageCount || currentTextureCount != lastTextureCount || currentVideoCount != lastVideoCount
            || builtWithVideoShader != (videoShader != null))
        {
            lastTextureUpdates = currentTextureUpdates;
            lastAtlasPageCount = currentAtlasPageCount;
            lastTextureAtlasPageCount = currentTextureAtlasPageCount;
            lastTextureCount = currentTextureCount;
            lastVideoCount = currentVideoCount;
            refreshTextures();
        }
    }

    private void updateHeader()
    {
        long liveBytes = TextureRegistry.LiveBytes;
        long peakBytes = GlobalStatistics.Get<long>("Textures", "Peak Bytes", StatisticKind.Gauge, StatisticUnit.Bytes).Value;
        long reclaimed = GlobalStatistics.Get<long>("Textures", "Reclaimed by GC", StatisticKind.Cumulative).Value;

        int slices = TextureRegistry.LiveSliceCount;

        liveText.Text = $"Live: {TextureRegistry.LiveCount} textures, {toMegabytes(liveBytes)} (peak {toMegabytes(peakBytes)})"
                        + (slices > 0 ? $" + {slices} atlas slices" : "")
                        + (reclaimed > 0 ? $" — {reclaimed} reclaimed by GC (a Dispose is being missed!)" : "");

        int textureBinds = GlobalStatistics.Get<int>("Renderer", "Texture Binds This Frame", StatisticKind.PerFrame).Value;

        summaryText.Text = $"Binds last frame: {textureBinds}"
                           + $"   Native: {toMegabytes(NativeMemoryTracker.TotalBytes)} (peak {toMegabytes(NativeMemoryTracker.PeakTotalBytes)})";

        categoryText.Text = $"tex {toMegabytes(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Textures))}"
                            + $"   fb {toMegabytes(NativeMemoryTracker.BytesFor(NativeMemoryCategory.FrameBuffers))}"
                            + $"   video {toMegabytes(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Video))}"
                            + $"   audio {toMegabytes(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Audio))}"
                            + $"   fonts {toMegabytes(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Fonts))}"
                            + $"   other {toMegabytes(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Other))}"
                            // mapped font files are a ceiling of file-backed pages the OS may never fault in,
                            // so folding them into Native would overstate a figure whose
                            // whole job is to be read against the process footprint. Referencing it here also forces the
                            // statistic to register, so "Fonts -> Mapped Bytes" reads 0 rather than being absent when
                            // nothing has been mapped.
                            + $"   (mapped {toMegabytes(NativeFileMapping.MappedBytes)})";
    }

    private static string toMegabytes(long bytes) => $"{bytes / 1024.0 / 1024.0:0.0} MB";

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
    /// Re-reads the upload state of every pooled video texture a card is showing. The previews
    /// themselves follow their texture on their own and need nothing from here.
    /// </summary>
    private void updateVideoLabels()
    {
        foreach (var (label, videoTexture) in videoStateLabels)
        {
            var state = describeVideoState(videoTexture);
            label.Text = state.Text;
            label.Color = state.Color;
        }

        foreach (var (label, pool) in videoPoolLabels)
        {
            int uploaded = pool.Count(vt => vt.UploadComplete);
            label.Text = $"Uploaded: {uploaded} / {pool.Count}";
            label.Color = uploaded == pool.Count ? Color.LimeGreen : Color.Yellow;
        }
    }

    /// <summary>
    /// Re-reads how often each card's texture was bound during the last completed frame.
    /// </summary>
    private void updateBindLabels()
    {
        foreach (var (label, counter) in bindLabels)
        {
            int binds = counter.LastFrame;

            label.Text = $"{binds} bind{(binds == 1 ? "" : "s")} last frame";
            label.Color = binds > 0 ? Color.LightGray : Color.Gray;
        }
    }

    /// <summary>
    /// A card's bind label, registered so <see cref="updateBindLabels"/> keeps it current. Returns null
    /// when there is nothing to count against, which is what an atlas slice or a dimension-only proxy is.
    /// </summary>
    private SpriteText? createBindLabel(TextureBindCounter? counter)
    {
        if (counter == null)
            return null;

        var label = new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Font = FontUsage.Default.With(size: 10),
            Color = Color.Gray,
            Text = "0 binds last frame"
        };

        bindLabels.Add((label, counter));
        return label;
    }

    private void refreshTextures()
    {
        flowContainer.Clear();
        videoStateLabels.Clear();
        videoPoolLabels.Clear();
        bindLabels.Clear();
        builtWithVideoShader = videoShader != null;

        var fontAtlas = fontStore.Atlas;

        var standalone = textureManager.GetAllTextures()
                                       .Where(t => t != null && !(fontAtlas?.OwnsNativeTexture(t.BackendTexture) ?? false))
                                       .OrderByDescending(t => (long)t.Width * t.Height)
                                       .ToList();

        foreach (var tex in standalone)
            flowContainer.Add(createTextureCard(describe(tex), tex));

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
                flowContainer.Add(createVideoPoolCard(group.Key.Width, group.Key.Height, pooled));

                for (int i = 0; i < pooled.Count; i++)
                    flowContainer.Add(createVideoTextureCard(pooled[i], i, pooled.Count));
            }
        }

        if (textureManager.Atlas != null)
        {
            int texturePageIndex = 0;
            foreach (var atlasPage in textureManager.Atlas.GetAllPages())
            {
                flowContainer.Add(createTextureCard($"Texture Atlas Page {texturePageIndex} ({atlasPage.Width}x{atlasPage.Height})", atlasPage));
                texturePageIndex++;
            }
        }

        if (fontAtlas != null)
        {
            int pageIndex = 0;
            foreach (var atlasPage in fontAtlas.GetAllPages())
            {
                flowContainer.Add(createTextureCard($"Font Atlas Page {pageIndex} ({atlasPage.Width}x{atlasPage.Height})", atlasPage));
                pageIndex++;
            }
        }
    }

    /// <summary>
    /// A card label for a standalone texture
    /// </summary>
    private static string describe(Texture texture)
    {
        long bytes = (long)texture.Width * texture.Height * 4;
        string size = $"{texture.Width}x{texture.Height}, {toMegabytes(bytes)}";
        string name = string.IsNullOrEmpty(texture.Name) ? "Texture" : texture.Name;

        return $"{name} ({size})";
    }

    private static (string Text, Color Color)? describeState(Texture texture)
    {
        var backend = texture.BackendTexture;

        if (backend == null)
            return ("proxy (no GPU texture)", Color.LightGray);

        if (backend.Handle == IntPtr.Zero)
            return ("GPU texture destroyed", Color.Red);

        if (!backend.Available)
            return ("uploading", Color.Yellow);

        return null;
    }

    /// <summary>
    /// A state line for one pooled video texture, describing whether its preview can show anything.
    /// </summary>
    private (string Text, Color Color) describeVideoState(IVideoTexture videoTexture)
    {
        if (videoShader == null)
            return ("compiling preview shader", Color.LightGray);

        if (videoTexture.IsDisposed)
            return ("disposed", Color.Red);

        // A pooled texture between frames keeps the last frame uploaded into it, so this is only really
        // seen before the first frame of a video arrives.
        if (!videoTexture.UploadComplete)
            return ("awaiting upload", Color.Yellow);

        return ("holding a frame", Color.LimeGreen);
    }

    /// <summary>
    /// The label-and-preview column inside a card.
    /// </summary>
    private static FlowContainer cardFlow(Drawable[] children) => new FlowContainer
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
    /// Creates an info-only summary card for one video texture pool. The individual textures in the
    /// pool follow it as <see cref="createVideoTextureCard"/> previews.
    /// </summary>
    private Drawable createVideoPoolCard(int width, int height, List<IVideoTexture> pool)
    {
        int total = pool.Count;
        int uploaded = pool.Count(vt => vt.UploadComplete);

        // The YUV planes, not width * height * 4: a video texture is three single-channel planes, so it
        // costs 1.5 bytes per pixel. The same figure the "video" total in the native memory line is built
        // from, so the two agree.
        long bytes = NativeTextureMemory.BytesForVideoPlanes(width, height);

        var uploadedText = new SpriteText
        {
            Text = $"Uploaded: {uploaded} / {total}",
            Font = FontUsage.Default.With(size: 10),
            Color = uploaded == total ? Color.LimeGreen : Color.Yellow
        };

        videoPoolLabels.Add((uploadedText, pool));

        return new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(card_size),
            // A card clips its own content: a preview or a label that misjudges the space left over
            // stays inside its card rather than drawing across the one next to it.
            Masking = true,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Black,
                    Alpha = 0.9f
                },
                cardFlow(new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Video Texture Pool",
                        Font = FontUsage.Default.With(size: 12, weight: "Bold"),
                        Color = Color.White
                    },
                    new SpriteText
                    {
                        Text = $"{width}x{height},",
                        Font = FontUsage.Default.With(size: 10),
                        Color = Color.LightGray
                    },
                    new SpriteText
                    {
                        Text = $"{toMegabytes(bytes)} each",
                        Font = FontUsage.Default.With(size: 10),
                        Color = Color.LightGray
                    },
                    new SpriteText
                    {
                        Text = $"Pool: {total} ({toMegabytes(bytes * total)})",
                        Font = FontUsage.Default.With(size: 10),
                        Color = Color.LightGray
                    },
                    uploadedText
                })
            }
        };
    }

    /// <summary>
    /// Creates a card previewing the frame a single pooled video texture is currently holding.
    /// The preview is live: it keeps following the texture as new frames are uploaded into it, so it
    /// does not need a refresh to update.
    /// </summary>
    private Container createVideoTextureCard(IVideoTexture videoTexture, int index, int total)
    {
        var state = describeVideoState(videoTexture);

        var stateText = new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Text = state.Text,
            Font = FontUsage.Default.With(size: 10),
            Color = state.Color
        };

        videoStateLabels.Add((stateText, videoTexture));

        var bindText = createBindLabel(videoTexture.Binds)!;

        // Three label lines above the preview: title, state, binds.
        var area = new Vector2(card_content_size, card_content_size - 3 * (card_label_height + card_label_spacing));

        // A video frame lives in YUV planes rather than a Texture, so FillMode has no aspect ratio to
        // letterbox against — scale the preview to the frame's aspect within the card by hand.
        float areaAspect = area.X / area.Y;
        float frameAspect = videoTexture.Height > 0 ? (float)videoTexture.Width / videoTexture.Height : areaAspect;

        var previewSize = frameAspect > areaAspect
            ? new Vector2(1, areaAspect / frameAspect)
            : new Vector2(frameAspect / areaAspect, 1);

        return new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(card_size),
            // A card clips its own content: a preview or a label that misjudges the space left over
            // stays inside its card rather than drawing across the one next to it.
            Masking = true,
            Children = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.DarkGray,
                    Alpha = 0.5f
                },
                cardFlow(new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = $"Video Pool Texture {index + 1}/{total} ({videoTexture.Width}x{videoTexture.Height}, {toMegabytes(NativeTextureMemory.BytesForVideoPlanes(videoTexture.Width, videoTexture.Height))})",
                        Font = FontUsage.Default.With(size: 10),
                        Color = Color.White,
                        // Truncated rather than left to the card's masking, so it ends in an
                        // ellipsis rather than mid-glyph.
                        Truncate = true,
                        MaxWidth = card_content_size
                    },
                    stateText,
                    bindText,
                    new Container
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Size = area,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.Black,
                                Alpha = 0.5f
                            },
                            new VideoTexturePreview(videoTexture, videoShader)
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                RelativeSizeAxes = Axes.Both,
                                Size = previewSize
                            }
                        }
                    }
                })
            }
        };
    }

    private Container createTextureCard(string title, Texture texture)
    {
        var state = describeState(texture);

        var labels = new List<Drawable>
        {
            new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = title,
                Font = FontUsage.Default.With(size: 10),
                Color = Color.White,
                // A texture's title is a full asset path, so most of them are wider than a card.
                // The card masks anyway, but a clipped title ends mid-glyph where a truncated one
                // ends in an ellipsis.
                Truncate = true,
                MaxWidth = card_content_size
            }
        };

        if (state != null)
        {
            labels.Add(new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = state.Value.Text,
                Font = FontUsage.Default.With(size: 10),
                Color = state.Value.Color
            });
        }

        var bindLabel = createBindLabel(texture.BackendTexture?.Binds);

        if (bindLabel != null)
            labels.Add(bindLabel);

        float previewHeight = card_content_size - labels.Count * (card_label_height + card_label_spacing);

        labels.Add(new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(card_content_size, previewHeight),
            Child = new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Texture = texture,
                Size = new Vector2(1),
                RelativeSizeAxes = Axes.Both,
                FillMode = TextureFillMode.Fit
            }
        });

        return new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(card_size),
            // A card clips its own content: a preview or a label that misjudges the space left over
            // stays inside its card rather than drawing across the one next to it.
            Masking = true,
            Children = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.DarkGray,
                    Alpha = 0.5f
                },
                cardFlow(labels.ToArray())
            }
        };
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
