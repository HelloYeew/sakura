// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// A plot of dots being built again each frame (a change of color always does), against faded, its vertices scaled
/// (<see cref="Drawable.UpdateVertexAlpha"/>), and revealed by a mask, its vertices kept (<see cref="Drawable.KeepVertices"/>).
/// One op is one frame, its draw node included.
/// </summary>
[MemoryDiagnoser]
public class VertexAlphaBenchmarks
{
    [Params(200, 2000)]
    public int Dots;

    private Container scalingRoot = null!;
    private ManualClock scalingClock = null!;
    private PlotCanvas scaling = null!;

    private Container buildingRoot = null!;
    private ManualClock buildingClock = null!;
    private PlotCanvas building = null!;

    private Container revealRoot = null!;
    private ManualClock revealClock = null!;
    private Container reveal = null!;

    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        (scalingRoot, scalingClock) = BenchmarkTree.CreateRoot();
        (buildingRoot, buildingClock) = BenchmarkTree.CreateRoot();

        scalingRoot.Add(scaling = plot(new PlotCanvas()));
        buildingRoot.Add(building = plot(new PlotCanvas()));

        scalingRoot.Load();
        scalingRoot.CompleteLoad();
        buildingRoot.Load();
        buildingRoot.CompleteLoad();

        (revealRoot, revealClock) = BenchmarkTree.CreateRoot();
        revealRoot.Add(reveal = new Container { Size = new Vector2(1200, 300), Masking = true, Child = plot(new PlotCanvas()) });
        revealRoot.Load();
        revealRoot.CompleteLoad();
    }

    private PlotCanvas plot(PlotCanvas canvas)
    {
        canvas.RelativeSizeAxes = Axes.None;
        canvas.Size = new Vector2(1200, 300);

        for (int i = 0; i < Dots; i++)
            canvas.Dot(new Vector2(i * 1200f / Dots, 150 + 100 * MathF.Sin(i * 0.1f)), 2.5f, Color.White);

        return canvas;
    }

    private float nextAlpha() => 0.25f + 0.75f * (MathF.Sin(frame * 0.05f) * 0.5f + 0.5f);

    [Benchmark(Baseline = true)]
    public DrawNode RecolorBuildingVertices()
    {
        frame++;
        building.Color = frame % 2 == 0 ? Color.White : Color.WhiteSmoke;
        buildingClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        buildingRoot.UpdateSubTree();
        return buildingRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    [Benchmark]
    public DrawNode FadeScalingVertices()
    {
        frame++;
        scaling.Alpha = nextAlpha();
        scalingClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        scalingRoot.UpdateSubTree();
        return scalingRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// A mask growing over a plot that stays where it is: a graph being revealed.
    /// </summary>
    [Benchmark]
    public DrawNode RevealUnderAGrowingMask()
    {
        frame++;
        reveal.Width = 600 + 500 * MathF.Sin(frame * 0.05f);
        revealClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        revealRoot.UpdateSubTree();
        return revealRoot.GenerateDrawNodeSubtree(frame % 3);
    }
}
