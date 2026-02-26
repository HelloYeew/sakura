// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Performance;

public class TextureViewerDisplay : Container, IRemoveFromDrawVisualiser
{
    private readonly FlowContainer flowContainer;
    private readonly ScrollableContainer scrollContainer;
    private readonly Container contentContainer;
    private readonly SpriteText currentTimeText;
    private readonly SpriteText runningTimeText;
    private readonly SpriteText bindsText;

    private int lastTextureUpdates = -1;
    private int lastAtlasPageCount = -1;
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
        AlwaysPresent = true;

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

        if (IsHidden) return;

        currentTimeText.Text = $"{DateTime.Now:dd MMMM yyyy HH:mm:ss tt}";
        runningTimeText.Text = $"Has been running for {TimeSpan.FromSeconds(host.AppClock.CurrentTime / 1000):hh\\:mm\\:ss}";

        int textureBinds = GlobalStatistics.Get<int>("Renderer", "Texture Binds").Value;
        bindsText.Text = $"Texture Binds (Last Frame): {textureBinds}";

        if (host.AppClock.CurrentTime - lastUpdateTime < 100)
            return;

        lastUpdateTime = host.AppClock.CurrentTime;

        int currentTextureUpdates = GlobalStatistics.Get<int>("Textures", "Texture Updates").Value;
        int currentAtlasPageCount = fontStore.Atlas != null ? fontStore.Atlas.GetAllPages().Count() : 0;

        if (currentTextureUpdates != lastTextureUpdates || currentAtlasPageCount != lastAtlasPageCount)
        {
            lastTextureUpdates = currentTextureUpdates;
            lastAtlasPageCount = currentAtlasPageCount;
            refreshTextures();
        }
    }

    private void refreshTextures()
    {
        flowContainer.Clear();

        foreach (var tex in textureManager.GetAllTextures())
        {
            if (tex == null) continue;
            flowContainer.Add(createTextureCard($"Texture ({tex.Width}x{tex.Height})", tex));
        }

        if (fontStore.Atlas != null)
        {
            int pageIndex = 0;
            foreach (var atlasPage in fontStore.Atlas.GetAllPages())
            {
                flowContainer.Add(createTextureCard($"Font Atlas Page {pageIndex} ({atlasPage.Width}x{atlasPage.Height})", atlasPage));
                pageIndex++;
            }
        }
    }

    private Drawable createTextureCard(string title, Texture texture)
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
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Text = title,
                            Font = FontUsage.Default.With(size: 14),
                            Color = Color.White
                        },
                        new Container
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Size = new Vector2(256, 256 - 20),
                            Child = new Sprite()
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Texture = texture,
                                Size = new Vector2(1),
                                RelativeSizeAxes = Axes.Both,
                                FillMode = TextureFillMode.Fit
                            }
                        }
                    }
                }
            }
        };
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Escape)
        {
            this.FadeOut(200, Easing.OutQuint);
            return true;
        }
        return base.OnKeyDown(e);
    }
}
