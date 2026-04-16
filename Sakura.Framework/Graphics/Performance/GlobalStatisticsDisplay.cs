// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Performance;

public class GlobalStatisticsDisplay : FocusedOverlayContainer, IRemoveFromDrawVisualiser
{
    private readonly FlowContainer groupsFlow;
    private readonly ScrollableContainer scrollContainer;
    private readonly Container contentContainer;
    private readonly Dictionary<string, FlowContainer> groupContainers = new Dictionary<string, FlowContainer>();
    private readonly Dictionary<IGlobalStatistic, SpriteText> statValueTexts = new Dictionary<IGlobalStatistic, SpriteText>();
    private readonly SpriteText currentTimeText;
    private readonly SpriteText runningTimeText;

    [Resolved]
    private AppHost host { get; set; }

    public GlobalStatisticsDisplay()
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
            Size = new Vector2(1),
            Color = Color.Black,
            Alpha = 0.75f
        });

        Add(new SpriteText
        {
            Text = "Global Statistics (Ctrl + F2)",
            Font = FontUsage.Default.With(size: 30, weight: "Bold"),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 5),
            Color = Color.Cyan,
            RelativeSizeAxes = Axes.X,
            Height = 50
        });

        Add(currentTimeText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 50),
            Color = Color.LightCyan,
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
            Color = Color.LightCyan,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(new SpriteText()
        {
            Text = $"Sakura Framework v{DebugUtils.GetFrameworkVersion()}",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 50),
            Color = Color.LightCyan,
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
            Color = Color.LightCyan,
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

        scrollContainer.Add(groupsFlow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Width = 1,
            Spacing = new Vector2(30, 15),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });
    }

    public override void Update()
    {
        base.Update();

        if (State == Visibility.Hidden) return;

        foreach (var stat in GlobalStatistics.GetStatistics())
        {
            if (!groupContainers.TryGetValue(stat.Group, out var groupFlow))
            {
                groupFlow = new FlowContainer
                {
                    Direction = FlowDirection.Vertical,
                    RelativeSizeAxes = Axes.None,
                    AutoSizeAxes = Axes.Y,
                    Width = 350,
                    Spacing = new Vector2(0, 2),
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Padding = new MarginPadding { Bottom = 10 }
                };

                groupFlow.Add(new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Text = stat.Group,
                    Font = FontUsage.Default.With(size: 20, weight: "Bold"),
                    Color = Color.Cyan
                });

                groupContainers[stat.Group] = groupFlow;
                groupsFlow.Add(groupFlow);
            }

            if (!statValueTexts.TryGetValue(stat, out var valueTextElement))
            {
                var rowContainer = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Size = new Vector2(1, 0)
                };

                rowContainer.Add(new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Text = stat.Name,
                    Font = FontUsage.Default.With(size: 14),
                    Color = Color.LightGray
                });

                valueTextElement = new SpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Font = FontUsage.Default.With(size: 14),
                    Color = Color.White
                };

                rowContainer.Add(valueTextElement);
                statValueTexts[stat] = valueTextElement;
                groupFlow.Add(rowContainer);
            }

            string newDisplayValue = stat.DisplayValue;
            if (valueTextElement.Text != newDisplayValue)
            {
                valueTextElement.Text = newDisplayValue;
            }
        }

        currentTimeText.Text = $"{DateTime.Now:dd MMMM yyyy HH:mm:ss tt}";
        runningTimeText.Text = $"Has been running for {TimeSpan.FromSeconds(host.AppClock.CurrentTime / 1000):hh\\:mm\\:ss}";
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            return true;
        }
        return base.OnKeyDown(e);
    }

    protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);
}
