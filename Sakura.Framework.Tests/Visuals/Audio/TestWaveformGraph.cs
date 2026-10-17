// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Audio;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Audio;

[TestFixture]
public partial class TestWaveformGraph : TestScene
{
    private Waveform? waveform;
    private WaveformGraph horizontal = null!;
    private WaveformGraph vertical = null!;
    private ScrollableContainer scroll = null!;

    [SetUp]
    public void SetUpSteps()
    {
        AddStep("Create graphs", () =>
        {
            waveform?.Dispose();
            waveform = new Waveform(Assembly.GetExecutingAssembly().GetManifestResourceStream("Sakura.Framework.Tests.Resources.Tracks.test.mp3")
                                    ?? throw new InvalidOperationException("Missing test track."));

            Clear();

            Add(scroll = new ScrollableContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                RelativeSizeAxes = Axes.X,
                Width = 0.9f,
                Height = 160,
                Y = 40,
                Direction = ScrollDirection.Horizontal,
                Masking = true,
                Child = horizontal = new WaveformGraph
                {
                    Size = new Vector2(8000, 160),
                    Waveform = waveform,
                    Color = ColorExtensions.FromHex("6b6b8a"),
                    LowColor = ColorExtensions.FromHex("ff7a59"),
                    MidColor = ColorExtensions.FromHex("7ad1ff"),
                    HighColor = ColorExtensions.FromHex("f7f0f4")
                }
            });

            // A vertical timeline is the same graph turned on its side: time runs down the screen.
            Add(new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Size = new Vector2(160, 360),
                Y = -20,
                Masking = true,
                Child = vertical = new WaveformGraph
                {
                    Size = new Vector2(360, 160),
                    Rotation = 90,
                    X = 160,
                    Waveform = waveform,
                    Color = ColorExtensions.FromHex("6b6b8a"),
                    LowColor = ColorExtensions.FromHex("ff7a59"),
                    MidColor = ColorExtensions.FromHex("7ad1ff"),
                    HighColor = ColorExtensions.FromHex("f7f0f4")
                }
            });
        });
    }

    [Test]
    public void TestZoom()
    {
        AddSliderStep("Width (zoom)", 1000f, 40000f, 8000f, width =>
        {
            if (horizontal != null)
                horizontal.Width = width;
        });

        AddSliderStep("Resolution", 0.1f, 2f, 1f, value =>
        {
            if (horizontal != null)
                horizontal.Resolution = value;
        });
    }

    [Test]
    public void TestColors()
    {
        AddStep("Base colour only", () =>
        {
            horizontal.LowColor = horizontal.MidColor = horizontal.HighColor = null;
            horizontal.Color = Color.White;
        });

        AddStep("Band colours", () =>
        {
            horizontal.Color = ColorExtensions.FromHex("6b6b8a");
            horizontal.LowColor = ColorExtensions.FromHex("ff7a59");
            horizontal.MidColor = ColorExtensions.FromHex("7ad1ff");
            horizontal.HighColor = ColorExtensions.FromHex("f7f0f4");
        });
    }
}
