// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Input;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Performance;

public partial class TextureViewerDisplay : FocusedOverlayContainer, IRemoveFromDrawVisualiser
{
    private readonly FlowContainer flowContainer;
    private readonly ScrollableContainer scrollContainer;
    private readonly Container contentContainer;
    private readonly SpriteText currentTimeText;
    private readonly SpriteText runningTimeText;
    private readonly SpriteText bindsText;
    private readonly SpriteText vramText;
    private readonly SpriteText nativeMemoryText;

    private int lastTextureUpdates = -1;
    private int lastAtlasPageCount = -1;
    private int lastTextureAtlasPageCount = -1;
    private int lastTextureCount = -1;
    private int lastVideoCount = -1;
    private double lastUpdateTime;

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
        RelativeSizeAxes = Axes.Both;
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        Size = new Vector2(1);

        Add(new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Size = new Vector2(1),
            Alpha = 0.75f
        });

        Add(new SpriteText
        {
            Text = "Texture & Atlas Viewer (Ctrl + F3)",
            Font = FontUsage.Default.With(size: 30, weight: "Bold"),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 5),
            Color = Color.LimeGreen,
            Height = 50
        });

        Add(currentTimeText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 50),
            Color = Color.LightGreen,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(runningTimeText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 70),
            Color = Color.LightGreen,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(bindsText = new SpriteText
        {
            Text = "Texture Binds (Last Frame): 0",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 90),
            Color = Color.LightGreen,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(new SpriteText()
        {
            Text =
                $"Sakura Framework v{DebugUtils.GetFrameworkVersion()}",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 50),
            Color = Color.LightGreen,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(new SpriteText()
        {
            Text = $"Running {Logger.AppIdentifier} v{Logger.VersionIdentifier} {(DebugUtils.IsDebugBuild ? "(Debug Build)" : "")}",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 70),
            Color = Color.LightGreen,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(vramText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 110),
            Color = Color.LightGreen,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(nativeMemoryText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 130),
            Color = Color.LightGreen,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(contentContainer = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1, 0.75f),
            Padding = new MarginPadding(20)
        });

        contentContainer.Add(new Box()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Alpha = 0.2f,
            Size = new Vector2(1)
        });

        contentContainer.Add(scrollContainer = new ScrollableContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        });

        scrollContainer.Add(flowContainer = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            Width = 1f,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(5),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        renderer = host.Renderer;
        refreshTextures();
    }

    public override void Update()
    {
        base.Update();

        if (State == Visibility.Hidden) return;

        currentTimeText.Text = $"{DateTime.Now:dd MMMM yyyy HH:mm:ss tt}";
        runningTimeText.Text = $"Has been running for {TimeSpan.FromSeconds(host.UpdateClock.CurrentTime / 1000):hh\\:mm\\:ss}";

        int textureBinds = GlobalStatistics.Get<int>("Renderer", "Texture Binds (Last Frame)").Value;
        bindsText.Text = $"Texture Binds (Last Frame): {textureBinds}";

        long liveBytes = TextureRegistry.LiveBytes;
        long peakBytes = GlobalStatistics.Get<long>("Textures", "Peak Bytes").Value;
        long reclaimed = GlobalStatistics.Get<long>("Textures", "Reclaimed by GC").Value;

        int slices = TextureRegistry.LiveSliceCount;

        vramText.Text = $"Live: {TextureRegistry.LiveCount} textures, {toMegabytes(liveBytes)} (peak {toMegabytes(peakBytes)})"
                        + (slices > 0 ? $"+ {slices} atlas slices" : "")
                        + (reclaimed > 0 ? $"— {reclaimed} reclaimed by GC (a Dispose is being missed!)" : "");

        nativeMemoryText.Text =
            $"Native: {toMegabytes(NativeMemoryTracker.TotalBytes)} (peak {toMegabytes(NativeMemoryTracker.PeakTotalBytes)})"
            + $"   tex {toMegabytes(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Textures))}"
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

        if (host.UpdateClock.CurrentTime - lastUpdateTime < 100)
            return;

        lastUpdateTime = host.UpdateClock.CurrentTime;

        updateVideoLabels();
        updateBindLabels();

        int currentTextureUpdates = GlobalStatistics.Get<int>("Textures", "Texture Updates").Value;
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
            Font = FontUsage.Default.With(size: 12),
            Color = uploaded == total ? Color.LimeGreen : Color.Yellow
        };

        videoPoolLabels.Add((uploadedText, pool));

        return new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(256, 256),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Black,
                    Alpha = 0.9f
                },
                new FlowContainer
                {
                    Direction = FlowDirection.Vertical,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Spacing = new Vector2(0, 6),
                    Padding = new MarginPadding(10),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = "Video Texture Pool",
                            Font = FontUsage.Default.With(size: 14, weight: "Bold"),
                            Color = Color.White
                        },
                        new SpriteText
                        {
                            Text = $"{width}x{height}, {toMegabytes(bytes)} each",
                            Font = FontUsage.Default.With(size: 12),
                            Color = Color.LightGray
                        },
                        new SpriteText
                        {
                            Text = $"Pool size: {total} ({toMegabytes(bytes * total)})",
                            Font = FontUsage.Default.With(size: 12),
                            Color = Color.LightGray
                        },
                        uploadedText
                    }
                }
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
        const float card_size = 256;
        const float padding = 5;
        const float spacing = 4;
        const float title_height = 20;
        const float state_height = 14;
        const float bind_height = 14;

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

        var area = new Vector2(card_size - padding * 2, card_size - padding * 2 - title_height - state_height - bind_height - spacing * 3);

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
                new FlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Spacing = new Vector2(0, spacing),
                    Padding = new MarginPadding(padding),
                    Direction = FlowDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Text = $"Video Pool Texture {index + 1}/{total} ({videoTexture.Width}x{videoTexture.Height}, {toMegabytes(NativeTextureMemory.BytesForVideoPlanes(videoTexture.Width, videoTexture.Height))})",
                            Font = FontUsage.Default.With(size: 10),
                            Color = Color.White,
                            // Nothing masks a card, so a title too long for one would draw over its neighbour.
                            Truncate = true,
                            MaxWidth = card_size - padding * 2
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
                    }
                }
            }
        };
    }

    private Container createTextureCard(string title, Texture texture)
    {
        var state = describeState(texture);

        const float title_height = 20;
        const float bind_height = 14;
        float stateHeight = state == null ? 0 : 14;

        var labels = new List<Drawable>
        {
            new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = title,
                Font = FontUsage.Default.With(size: 10),
                Color = Color.White
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

        labels.Add(new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(256, 256 - title_height - stateHeight - (bindLabel == null ? 0 : bind_height)),
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
            Size = new Vector2(256, 256),
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
                new FlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Spacing = new Vector2(0, 5),
                    Padding = new MarginPadding(5),
                    Children = labels.ToArray()
                }
            }
        };
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (State == Visibility.Visible && e.Key == Key.Escape)
        {
            Hide();
            return true;
        }
        return base.OnKeyDown(e);
    }

    protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);

    protected override void Dispose(bool isDisposing)
    {
        if (IsDisposed) return;

        // The preview cards borrow this shader, they never own it — releasing it here is the only
        // disposal, and it must happen on the draw thread.
        if (videoShader != null)
        {
            var shader = videoShader;
            videoShader = null;
            renderer?.ScheduleToDrawThread(shader.Dispose);
        }

        base.Dispose(isDisposing);
    }
}
