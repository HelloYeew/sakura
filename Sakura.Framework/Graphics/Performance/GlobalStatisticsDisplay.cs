// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Performance;

public partial class GlobalStatisticsDisplay : DebugWindow
{
    /// <summary>
    /// How often the values are re-read.
    /// </summary>
    private const double refresh_interval = 100;

    private const float group_width = 350;

    protected override string Title => "Global Statistics (Ctrl + F2)";
    protected override Vector2 DefaultSize => new Vector2(780, 520);
    protected override Vector2 MinSize => new Vector2(380, 200);
    protected override Color Accent => Color.Cyan;

    private readonly FlowContainer groupsFlow;

    private readonly Dictionary<string, FlowContainer> groupContainers = new Dictionary<string, FlowContainer>();
    private readonly Dictionary<IGlobalStatistic, SpriteText> statValueTexts = new Dictionary<IGlobalStatistic, SpriteText>();

    /// <summary>
    /// Each group's rows in display order, so a statistic that registers after its group was already built
    /// can be put in its alphabetical place rather than appended.
    /// </summary>
    private readonly Dictionary<string, List<(string Name, Drawable Row)>> groupRows = new Dictionary<string, List<(string, Drawable)>>();

    private double nextRefreshTime = double.MinValue;

    public GlobalStatisticsDisplay()
    {
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

        Add(scrollContainer);
    }

    /// <summary>
    /// Puts a group's rows back into alphabetical order after one arrived out of turn.
    /// </summary>
    private static void reorderRows(FlowContainer groupFlow, List<(string Name, Drawable Row)> rows)
    {
        rows.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (var row in rows)
            groupFlow.Remove(row.Row, dispose: false);

        foreach (var row in rows)
            groupFlow.Add(row.Row);
    }

    public override void Update()
    {
        base.Update();

        if (Clock.CurrentTime < nextRefreshTime)
            return;

        nextRefreshTime = Clock.CurrentTime + refresh_interval;

        foreach (var stat in GlobalStatistics.GetStatistics())
        {
            if (!groupContainers.TryGetValue(stat.Group, out var groupFlow))
                groupFlow = addGroup(stat.Group);

            if (!statValueTexts.TryGetValue(stat, out var valueTextElement))
                valueTextElement = addRow(stat, groupFlow);

            string newDisplayValue = stat.DisplayValue;

            if (valueTextElement.Text != newDisplayValue)
                valueTextElement.Text = newDisplayValue;
        }
    }

    private FlowContainer addGroup(string group)
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

        groupFlow.Add(new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Text = group,
            Font = FontUsage.Default.With(size: 20, weight: "Bold"),
            Color = Accent
        });

        groupContainers[group] = groupFlow;
        groupsFlow.Add(groupFlow);

        return groupFlow;
    }

    private SpriteText addRow(IGlobalStatistic stat, FlowContainer groupFlow)
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
            Color = Color.White
        };

        rowContainer.Add(valueTextElement);
        statValueTexts[stat] = valueTextElement;
        groupFlow.Add(rowContainer);

        if (!groupRows.TryGetValue(stat.Group, out var rows))
            groupRows[stat.Group] = rows = new List<(string, Drawable)>();

        rows.Add((stat.Name, rowContainer));

        // Rows arrive in order, so the common case is that the one just appended already belongs
        // last and there is nothing to do.
        if (rows.Count > 1 && string.CompareOrdinal(stat.Name, rows[^2].Name) < 0)
            reorderRows(groupFlow, rows);

        return valueTextElement;
    }
}
