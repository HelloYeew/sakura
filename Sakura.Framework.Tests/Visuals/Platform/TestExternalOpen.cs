// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Path = System.IO.Path;

namespace Sakura.Framework.Tests.Visuals.Platform;

public partial class TestExternalOpen : TestScene
{
    [Resolved]
    private AppHost host { get; set; }

    private TextFlowContainer outcomeFlow;

    private const float panel_width = 560;

    private string tempFilePath;

    [Test]
    public void TestExternalHandlers()
    {
        AddStep("build UI", buildUi);

        AddLabel("URLs");

        AddStep("open sakura repo", () => openUrl("https://github.com/HelloYeew/sakura"));
        AddStep("open mailto:", () => openUrl("mailto:hello@example.com"));
        AddStep("open invalid url (expects error)", () => openUrl("not-a-real-url"));

        AddLabel("Files");

        AddStep("create temp file", () =>
        {
            tempFilePath = Path.Combine(Path.GetTempPath(), "sakura-external-open-demo.txt");
            File.WriteAllText(tempFilePath, "hewwo yu mai wai laew");
            setOutcome($"Created: {wrappable(tempFilePath)}", Color.LightGreen);
        });

        AddStep("open temp file externally", () =>
        {
            if (!ensureTempFile())
                return;

            try
            {
                bool ok = host.OpenFileExternally(tempFilePath);
                setOutcome(ok ? $"Opened: {wrappable(tempFilePath)}" : "OpenFileExternally returned false", ok ? Color.LightGreen : Color.Orange);
            }
            catch (Exception ex)
            {
                setOutcome($"Error: {ex.Message}", Color.Red);
            }
        });

        AddStep("reveal temp file (present)", () =>
        {
            if (!ensureTempFile())
                return;

            try
            {
                bool ok = host.PresentFileExternally(tempFilePath);
                setOutcome(ok ? $"Revealed in file manager: {wrappable(tempFilePath)}" : "PresentFileExternally returned false", ok ? Color.LightGreen : Color.Orange);
            }
            catch (Exception ex)
            {
                setOutcome($"Error: {ex.Message}", Color.Red);
            }
        });
    }

    private void openUrl(string url)
    {
        try
        {
            host.OpenUrlExternally(url);
            setOutcome($"Requested: {wrappable(url)}", Color.LightGreen);
        }
        catch (Exception ex)
        {
            setOutcome($"Rejected \"{url}\": {ex.Message}", Color.Orange);
        }
    }

    private bool ensureTempFile()
    {
        if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
            return true;

        setOutcome("Run \"create temp file\" first.", Color.Orange);
        return false;
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
                        Text = "External open playground",
                        Color = Color.White,
                        Font = FontUsage.Default.With(size: 22),
                    },
                    new SpriteText
                    {
                        Text = "Steps launch your browser / mail client / file manager. Result shows below.",
                        Color = Color.LightGray,
                        Font = FontUsage.Default.With(size: 14),
                    },
                    outcomeFlow = new TextFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        AutoSizeAxes = Axes.Y,
                    },
                },
            },
        });

        setOutcome("Awaiting an action…", Color.Yellow);
    }

    private void setOutcome(string text, Color color)
    {
        if (outcomeFlow == null)
            return;

        outcomeFlow.Clear();
        outcomeFlow.AddText(text, t =>
        {
            t.Color = color;
            t.Font = FontUsage.Default.With(size: 16);
        });
    }

    private static string wrappable(string value) => value.Replace("/", "/ ").Replace("\\", "\\ ");
}
