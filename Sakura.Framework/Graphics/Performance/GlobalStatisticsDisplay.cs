// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Performance;

public partial class GlobalStatisticsDisplay : DebugWindow
{
    /// <summary>
    /// How often the values are re-read.
    /// </summary>
    private const double refresh_interval = 100;

    private const float group_width = 350;

    private const float toolbar_height = 34;

    private const string mark_baseline_text = "Mark baseline";
    private const string clear_baseline_text = "Clear baseline";

    /// <summary>
    /// How long a value takes to decay from <see cref="live_color"/> back to <see cref="resting_color"/>
    /// after it last changed. Comfortably longer than <see cref="refresh_interval"/>, so a value that
    /// keeps changing never finishes decaying and simply stays lit.
    /// </summary>
    private const double decay_duration = 900;

    // FromArgb rather than Color.White: a named color carries its KnownColor, so it compares unequal
    // to the identical value the lerp produces and every first decay step would invalidate for nothing.
    private static readonly Color live_color = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color resting_color = Color.FromArgb(255, 124, 124, 124);

    private static readonly Color delta_up_color = Color.FromArgb(255, 232, 174, 92);
    private static readonly Color delta_down_color = Color.FromArgb(255, 104, 190, 214);

    /// <summary>
    /// Gap between a value and the delta sitting to its left.
    /// </summary>
    private const float delta_gap = 8;

    protected override string Title => "Global Statistics (Ctrl + F2)";
    protected override Vector2 DefaultSize => new Vector2(780, 520);
    protected override Vector2 MinSize => new Vector2(380, 200);
    protected override Color Accent => Color.Cyan;

    private readonly FlowContainer groupsFlow;
    private readonly SpriteText emptyHint;

    private BasicTextBox search = null!;
    private BasicButton baselineButton = null!;

    private readonly Dictionary<IGlobalStatistic, double> baseline = new Dictionary<IGlobalStatistic, double>();

    private bool baselineActive;

    private readonly Dictionary<string, StatGroup> groups = new Dictionary<string, StatGroup>();

    private readonly List<string> groupOrder = new List<string>();

    private readonly Dictionary<IGlobalStatistic, StatRow> statRows = new Dictionary<IGlobalStatistic, StatRow>();

    private string filter = string.Empty;

    /// <summary>
    /// Set when the set of rows or the filter changes, so the flows are reconciled once at the end of
    /// a refresh rather than once per statistic that happened to arrive during it.
    /// </summary>
    private bool layoutDirty;

    private double nextRefreshTime = double.MinValue;

    public GlobalStatisticsDisplay()
    {
        Add(buildToolbar());

        var body = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding { Top = toolbar_height }
        };

        body.Add(emptyHint = new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(12, 10),
            Font = FontUsage.Default.With(size: 12),
            Color = Color.LightCyan,
            Alpha = 0
        });

        var scrollContainer = new ScrollableContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        };

        scrollContainer.Add(groupsFlow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Width = 1,
            Spacing = new Vector2(30, 15),
            Padding = new MarginPadding(10),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        body.Add(scrollContainer);

        Add(body);
    }

    private Drawable buildToolbar()
    {
        var toolbar = new Container
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            Height = toolbar_height,
            Padding = new MarginPadding(4)
        };

        baselineButton = new BasicButton
        {
            Text = mark_baseline_text,
            TextSize = 12,
            Size = new Vector2(120, 26),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            DefaultColor = Color.Teal,
            HoverColor = Color.DarkCyan,
            Action = toggleBaseline
        };

        toolbar.Add(baselineButton);

        search = createSearchBox();
        search.Text.ValueChanged += e =>
        {
            filter = e.NewValue ?? string.Empty;
            layoutDirty = true;
            nextRefreshTime = double.MinValue;
        };

        toolbar.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding { Left = 130 },
            Child = search
        });

        return toolbar;
    }

    private static BasicTextBox createSearchBox() => new BasicTextBox
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        RelativeSizeAxes = Axes.Both,
        Size = new Vector2(1),
        PlaceholderText = "Filter statistics by name or group",
        BackgroundColor = Color.FromArgb(255, 24, 34, 36),
        BackgroundFocusedColor = Color.FromArgb(255, 38, 60, 64),
        ReleaseFocusOnCommit = false
    };

    public Reactive<string> SearchText => search.Text;

    public override void Update()
    {
        base.Update();

        if (Clock.CurrentTime < nextRefreshTime)
            return;

        nextRefreshTime = Clock.CurrentTime + refresh_interval;

        foreach (var stat in GlobalStatistics.GetStatistics())
        {
            if (!groups.TryGetValue(stat.Group, out var group))
                group = addGroup(stat.Group);

            if (!statRows.TryGetValue(stat, out var row))
                row = addRow(stat, group);

            string display = stat.DisplayValue;
            var delta = renderDelta(stat);

            if (row.Value.Text != display)
            {
                row.Value.Text = display;
                row.LastChanged = Clock.CurrentTime;
                row.Decaying = true;
            }

            if (delta == null)
            {
                row.Delta.Text = string.Empty;
                row.Delta.Alpha = 0;
            }
            else
            {
                row.Delta.Text = delta.Value.Text;
                row.Delta.Color = delta.Value.Color;
                row.Delta.Alpha = 1;
                row.Delta.X = -(row.Value.Size.X + delta_gap);
            }

            if (row.Decaying)
                applyDecay(row);
        }

        if (!layoutDirty)
            return;

        layoutDirty = false;
        rebuildLayout();
    }

    private void applyDecay(StatRow row)
    {
        double age = Clock.CurrentTime - row.LastChanged;
        float t = (float)Math.Clamp(age / decay_duration, 0, 1);

        row.Value.Color = ColorExtensions.Lerp(live_color, resting_color, t);

        if (t >= 1)
            row.Decaying = false;
    }

    /// <summary>
    /// Whether values are currently being reported against a marked baseline.
    /// </summary>
    public bool HasBaseline => baselineActive;

    /// <summary>
    /// Marks the current values as the baseline or drops the one already marked.
    /// </summary>
    public void ToggleBaseline() => toggleBaseline();

    private void toggleBaseline()
    {
        baselineActive = !baselineActive;
        baseline.Clear();

        baselineButton.Text = baselineActive ? clear_baseline_text : mark_baseline_text;

        // Re-read on the next frame so the suffixes appear or disappear at once rather than as each
        // value happens to move.
        foreach (var row in statRows.Values)
        {
            row.Delta.Text = string.Empty;
            row.Delta.Alpha = 0;
        }

        nextRefreshTime = double.MinValue;
    }

    /// <summary>
    /// What a statistic has moved by since the baseline, or null when there is nothing to report.
    /// </summary>
    private (string Text, Color Color)? renderDelta(IGlobalStatistic stat)
    {
        if (!baselineActive)
            return null;

        if (stat.Kind == StatisticKind.PerFrame)
            return null;

        double? numeric = stat.NumericValue;

        if (numeric == null)
            return null;

        if (!baseline.TryGetValue(stat, out double from))
        {
            baseline[stat] = numeric.Value;
            return null;
        }

        double delta = numeric.Value - from;

        if (delta == 0)
            return null;

        string formatted = delta == Math.Floor(delta) ? Math.Abs(delta).ToString("N0") : Math.Abs(delta).ToString("N2");
        bool up = delta > 0;

        return ($"{(up ? "+" : "-")}{formatted}", up ? delta_up_color : delta_down_color);
    }

    private bool matches(string group, string name)
        => filter.Length == 0
           || group.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reconciles both flows against the groups, their rows, and the filter.
    /// </summary>
    private void rebuildLayout()
    {
        groupOrder.Sort(StringComparer.Ordinal);

        foreach (var groupFlow in currentChildren(groupsFlow))
            groupsFlow.Remove(groupFlow, dispose: false);

        int shownGroups = 0;

        foreach (string name in groupOrder)
        {
            var group = groups[name];

            foreach (var row in currentChildren(group.Flow))
            {
                if (row != group.Header)
                    group.Flow.Remove(row, dispose: false);
            }

            int shownRows = 0;

            foreach (var row in group.Rows)
            {
                if (!matches(name, row.Name))
                    continue;

                group.Flow.Add(row.Row);
                shownRows++;
            }

            if (shownRows == 0)
                continue;

            groupsFlow.Add(group.Flow);
            shownGroups++;
        }

        if (shownGroups == 0 && filter.Length > 0)
        {
            emptyHint.Text = $"No statistic matches \"{filter}\"";
            emptyHint.Alpha = 1;
        }
        else
            emptyHint.Alpha = 0;
    }

    /// <summary>
    /// A snapshot of a container's children, so the caller can remove from it while iterating.
    /// </summary>
    private static List<Drawable> currentChildren(Container container) => new List<Drawable>(container.Children);

    private StatGroup addGroup(string name)
    {
        var groupFlow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            RelativeSizeAxes = Axes.None,
            AutoSizeAxes = Axes.Y,
            Width = group_width,
            Spacing = new Vector2(0, 2),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Padding = new MarginPadding { Bottom = 10 }
        };

        var header = new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Text = name,
            Font = FontUsage.Default.With(size: 20, weight: "Bold"),
            Color = Accent
        };

        groupFlow.Add(header);

        var group = new StatGroup(name, groupFlow, header);

        groups[name] = group;
        groupOrder.Add(name);
        layoutDirty = true;

        return group;
    }

    private StatRow addRow(IGlobalStatistic stat, StatGroup group)
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

        var valueTextElement = new SpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Font = FontUsage.Default.With(size: 14),
            Color = resting_color
        };

        var deltaTextElement = new SpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Font = FontUsage.Default.With(size: 14),
            Color = delta_up_color,
            Alpha = 0
        };

        rowContainer.Add(valueTextElement);
        rowContainer.Add(deltaTextElement);

        var row = new StatRow(valueTextElement, deltaTextElement);
        statRows[stat] = row;

        group.Rows.Add((stat.Name, rowContainer));
        group.Rows.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        layoutDirty = true;

        return row;
    }

    /// <summary>
    /// A statistic's value text and when it last moved.
    /// </summary>
    private sealed class StatRow
    {
        public readonly SpriteText Value;

        /// <summary>
        /// Sits to the left of <see cref="Value"/>, in its own color, so "what this is now" and "what
        /// it has done since the baseline" never share a channel.
        /// </summary>
        public readonly SpriteText Delta;

        public double LastChanged;
        public bool Decaying;

        public StatRow(SpriteText value, SpriteText delta)
        {
            Value = value;
            Delta = delta;
        }
    }

    /// <summary>
    /// One group's chrome and the rows under it, held in display order.
    /// </summary>
    private sealed class StatGroup
    {
        public readonly FlowContainer Flow;
        public readonly Drawable Header;
        public readonly List<(string Name, Drawable Row)> Rows = new List<(string, Drawable)>();

        public StatGroup(string name, FlowContainer flow, Drawable header)
        {
            Name = name;
            Flow = flow;
            Header = header;
        }

        public string Name { get; }
    }
}
