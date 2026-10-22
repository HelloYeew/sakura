// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// A button for each debug tool, for a touchscreen, where there are no Ctrl+F keys to open them with.
/// </summary>
internal partial class DebugToolsWindow : DebugWindow
{
    private const float button_height = 40;
    private const float spacing = 6;

    protected override string Title => "Debug tools";
    protected override Vector2 DefaultSize { get; }
    protected override Vector2 MinSize => new Vector2(200, 120);
    protected override Color Accent => Color.LightPink;

    /// <param name="tools">Each tool's label and what opening or switching it does.</param>
    public DebugToolsWindow(IReadOnlyList<(string Label, Action Action)> tools)
    {
        DefaultSize = new Vector2(240, TITLE_HEIGHT + tools.Count * (button_height + spacing) + spacing);
        Size = DefaultSize;

        var flow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Size = new Vector2(1, 0),
            Spacing = new Vector2(0, spacing),
            Padding = new MarginPadding(spacing)
        };

        foreach (var (label, action) in tools)
        {
            flow.Add(new BasicButton
            {
                Text = label,
                RelativeSizeAxes = Axes.X,
                Size = new Vector2(1, button_height),
                Action = action
            });
        }

        Add(new ScrollableContainer
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Child = flow
        });
    }
}
