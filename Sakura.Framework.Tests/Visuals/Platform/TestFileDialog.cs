// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Platform.Dialogs;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Platform;

public partial class TestFileDialog : TestScene
{
    [Resolved]
    private AppHost host { get; set; }

    private TextFlowContainer outcomeFlow;
    private FlowContainer logColumn;

    private const float panel_width = 560;

    private static readonly FileDialogFilter[] image_filters =
    {
        new FileDialogFilter("Images", "png", "jpg", "jpeg", "gif", "bmp"),
        new FileDialogFilter("Text", "txt", "md"),
        FileDialogFilter.AllFiles(),
    };

    [Test]
    public void TestDialogs()
    {
        AddStep("build UI", buildUi);

        AddStep("open single file", () =>
            host.ShowOpenFileDialog(new FileDialogOptions
            {
                Title = "Pick a file",
                Filters = image_filters,
            }, onResult)
        );

        AddStep("open multiple files", () =>
            host.ShowOpenFileDialog(new FileDialogOptions
            {
                Title = "Pick one or more files",
                Filters = image_filters,
                AllowMultiple = true,
            }, onResult)
        );

        AddStep("save file", () =>
            host.ShowSaveFileDialog(new FileDialogOptions
            {
                Title = "Save as…",
                DefaultLocation = "untitled.txt",
                Filters = new[] { new FileDialogFilter("Text", "txt") },
            }, onResult)
        );

        AddStep("open folder", () =>
            host.ShowOpenFolderDialog(new FileDialogOptions
            {
                Title = "Pick a folder",
            }, onResult)
        );

        AddStep("open multiple folders", () =>
            host.ShowOpenFolderDialog(new FileDialogOptions
            {
                Title = "Pick one or more folders",
                AllowMultiple = true,
            }, onResult)
        );
    }

    private void buildUi()
    {
        Clear();

        Add(new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Width = panel_width,
            AutoSizeAxes = Axes.Y,
            Child = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 12),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Use the steps on the right to open dialogs. Results show below.",
                        Color = Color.LightGray,
                        Font = FontUsage.Default.With(size: 14),
                    },
                    outcomeFlow = new TextFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        AutoSizeAxes = Axes.Y,
                    },
                    new SpriteText
                    {
                        Text = "History (newest last):",
                        Color = Color.White,
                        Font = FontUsage.Default.With(size: 14),
                    },
                    logColumn = new FlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 4),
                    }
                },
            },
        });

        setOutcome("Awaiting a dialog...", Color.Yellow, 16);
    }

    private void onResult(FileDialogResult result)
    {
        string summary;
        Color color;

        if (result.Error != null)
        {
            summary = $"Error: {result.Error}";
            color = Color.Red;
        }
        else if (!result.Successful)
        {
            summary = "Cancelled";
            color = Color.Orange;
        }
        else if (result.Paths.Count == 1)
        {
            summary = $"Selected: {wrappable(result.Path)}";
            color = Color.LightGreen;
        }
        else
        {
            summary = $"Selected {result.Paths.Count}:  {string.Join("    ", result.Paths.Select(wrappable))}";
            color = Color.LightGreen;
        }

        setOutcome(summary, color, 16);

        var entry = new TextFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            AutoSizeAxes = Axes.Y,
        };

        logColumn?.Add(entry);
        entry.AddText(summary, t =>
        {
            t.Color = color;
            t.Font = FontUsage.Default.With(size: 13);
        });
    }

    private void setOutcome(string text, Color color, float size)
    {
        if (outcomeFlow == null)
            return;

        outcomeFlow.Clear();
        outcomeFlow.AddText(text, t =>
        {
            t.Color = color;
            t.Font = FontUsage.Default.With(size: size);
        });
    }

    private static string wrappable(string path) => path.Replace("/", "/ ").Replace("\\", "\\ ");
}
