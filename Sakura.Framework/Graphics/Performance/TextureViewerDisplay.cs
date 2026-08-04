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
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
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

    public override void LoadComplete()
    {
        base.LoadComplete();
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

        int currentTextureUpdates = GlobalStatistics.Get<int>("Textures", "Texture Updates").Value;
        int currentAtlasPageCount = fontStore.Atlas != null ? fontStore.Atlas.GetAllPages().Count() : 0;
        int currentTextureAtlasPageCount = textureManager.Atlas?.PageCount ?? 0;
        int currentTextureCount = textureManager.GetAllTextures().Count();
        int currentVideoCount = textureManager.GetAllVideoTextures().Count();

        if (currentTextureUpdates != lastTextureUpdates || currentAtlasPageCount != lastAtlasPageCount || currentTextureAtlasPageCount != lastTextureAtlasPageCount || currentTextureCount != lastTextureCount || currentVideoCount != lastVideoCount)
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

    private void refreshTextures()
    {
        flowContainer.Clear();

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
            var groups = videoTextures
                .GroupBy(vt => (vt.Width, vt.Height));

            foreach (var group in groups)
            {
                int groupUploaded = group.Count(vt => vt.UploadComplete);
                flowContainer.Add(createVideoPoolCard(group.Key.Width, group.Key.Height, group.Count(), groupUploaded));
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
    /// Creates an info-only card for a video texture.
    /// </summary>
    private Drawable createVideoPoolCard(int width, int height, int total, int uploaded)
    {
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
                            Text = $"Pool size: {total}",
                            Font = FontUsage.Default.With(size: 12),
                            Color = Color.LightGray
                        },
                        new SpriteText
                        {
                            Text = $"Uploaded: {uploaded} / {total}",
                            Font = FontUsage.Default.With(size: 12),
                            Color = uploaded == total ? Color.LimeGreen : Color.Yellow
                        }
                    }
                }
            }
        };
    }

    private Drawable createTextureCard(string title, Texture texture)
    {
        var state = describeState(texture);

        const float title_height = 20;
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

        labels.Add(new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(256, 256 - title_height - stateHeight),
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
}
