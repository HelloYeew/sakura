// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Audio;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Tests.Audio;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="WaveformGraph"/> builds geometry only for what is on screen, so a timeline can make it
/// as wide as the whole song.
/// </summary>
[TestFixture]
public class WaveformGraphTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private Container mask = null!;
    private WaveformGraph graph = null!;
    private Waveform waveform = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        waveform = new Waveform(WaveformTest.Wav(WaveformTest.Sine(440, 4)));

        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual),
            Child = mask = new Container
            {
                Size = new Vector2(400, 200),
                Masking = true,
                Child = graph = new WaveformGraph
                {
                    Size = new Vector2(4000, 200),
                    Waveform = waveform
                }
            }
        };

        root.Load();
        root.CompleteLoad();
    }

    [TearDown]
    public void TearDown() => waveform.Dispose();

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private void waitForPoints()
    {
        bool regenerated = false;
        graph.Regenerated += () => regenerated = true;

        var timeout = Stopwatch.StartNew();

        while (!regenerated && timeout.ElapsedMilliseconds < 10_000)
        {
            frame();
            Thread.Sleep(5);
        }

        Assert.That(regenerated, Is.True, "The graph never put its resampled points in place.");

        frame();
    }

    [Test]
    public void OnlyTheVisibleColumnsAreBuilt()
    {
        waitForPoints();

        // 4000 points across 4000 units, 400 of them inside the mask: about 400 quads, not 4000.
        Assert.That(graph.VertexCount / 4, Is.InRange(390, 410));
    }

    [Test]
    public void ScrollingRebuildsForTheNewlyVisiblePart()
    {
        waitForPoints();

        graph.X = -3800;
        frame();

        Assert.That(graph.VertexCount / 4, Is.InRange(190, 210), "Only the last 200 units are still under the mask.");
    }

    [Test]
    public void ResizingTheMaskRebuildsEvenThoughTheGraphDidNotMove()
    {
        waitForPoints();

        mask.Width = 800;
        frame();
        frame();

        Assert.That(graph.VertexCount / 4, Is.InRange(790, 810));
    }

    [Test]
    public void AGraphWithNoWaveformDrawsNothing()
    {
        graph.Waveform = null;
        frame();
        frame();

        Assert.That(graph.VertexCount, Is.Zero);
    }
}
