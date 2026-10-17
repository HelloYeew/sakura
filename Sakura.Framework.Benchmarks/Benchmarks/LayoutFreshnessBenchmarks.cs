// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// The costs that reading sizes fresh and laying out around children add: reading <c>DrawSize</c> at the
/// end of a relative chain, reading an auto-sizing container's size after something inside it changed,
/// and frames of a UI-shaped tree (auto-sized panels holding flows) where one thing changes per frame.
/// </summary>
[MemoryDiagnoser]
public class LayoutFreshnessBenchmarks
{
    private Container relativeRoot = null!;
    private ManualClock relativeClock = null!;
    private Drawable relativeLeaf = null!;

    private Container nestedRoot = null!;
    private ManualClock nestedClock = null!;
    private Container nestedOuter = null!;
    private Box nestedLeaf = null!;

    private Container uiRoot = null!;
    private ManualClock uiClock = null!;
    private Box uiChangingBox = null!;

    private Container anchoredRoot = null!;
    private ManualClock anchoredClock = null!;
    private Box anchoredChangingBox = null!;

    private bool flip;

    [GlobalSetup]
    public void Setup()
    {
        // Ten levels of relative containers with padding, a box at the bottom.
        (relativeRoot, relativeClock) = BenchmarkTree.CreateRoot();
        relativeLeaf = new Box { RelativeSizeAxes = Axes.Both };
        Drawable current = relativeLeaf;
        for (int i = 0; i < 10; i++)
            current = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(2), Child = current };
        relativeRoot.Add(current);
        BenchmarkTree.LoadAndSettle(relativeRoot, relativeClock);

        // Five levels of auto-sizing containers around a box.
        (nestedRoot, nestedClock) = BenchmarkTree.CreateRoot();
        nestedLeaf = new Box { Size = new Vector2(50, 20) };
        current = nestedLeaf;
        for (int i = 0; i < 5; i++)
            current = new Container { AutoSizeAxes = Axes.Both, Padding = new MarginPadding(1), Child = current };
        nestedOuter = (Container)current;
        nestedRoot.Add(nestedOuter);
        BenchmarkTree.LoadAndSettle(nestedRoot, nestedClock);

        // A settings-like page: a column of 30 auto-sized rows, each a flow of a label-sized box and a
        // control-sized box with a centred icon-sized one.
        (uiRoot, uiClock) = BenchmarkTree.CreateRoot();
        var column = new FlowContainer { Direction = FlowDirection.Vertical, RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Spacing = new Vector2(0, 8) };
        for (int i = 0; i < 30; i++)
        {
            var row = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding(6),
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both },
                    new FlowContainer
                    {
                        Direction = FlowDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(10, 0),
                        Children = new Drawable[]
                        {
                            new Box { Size = new Vector2(24, 24), Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                            i == 15 ? uiChangingBox = new Box { Size = new Vector2(160, 20) } : new Box { Size = new Vector2(160, 20) },
                        }
                    },
                    new Box { Size = new Vector2(200, 30), Anchor = Anchor.TopRight, Origin = Anchor.TopRight }
                }
            };
            column.Add(row);
        }
        uiRoot.Add(column);
        BenchmarkTree.LoadAndSettle(uiRoot, uiClock);

        // One auto-sizing container around 50 children placed by assorted anchors.
        (anchoredRoot, anchoredClock) = BenchmarkTree.CreateRoot();
        var anchored = new Container { AutoSizeAxes = Axes.Both };
        Anchor[] anchors = { Anchor.TopLeft, Anchor.Centre, Anchor.BottomRight, Anchor.TopCentre, Anchor.CentreRight };
        for (int i = 0; i < 50; i++)
        {
            var box = new Box { Size = new Vector2(10 + i, 10 + i % 7), Position = new Vector2(i % 9, i % 5), Anchor = anchors[i % anchors.Length], Origin = anchors[(i + 2) % anchors.Length] };
            if (i == 25)
                anchoredChangingBox = box;
            anchored.Add(box);
        }
        anchoredRoot.Add(anchored);
        BenchmarkTree.LoadAndSettle(anchoredRoot, anchoredClock);
    }

    /// <summary>
    /// Reads <c>DrawSize</c> at the bottom of a ten-deep relative chain 100 times.
    /// </summary>
    [Benchmark]
    public float ReadDrawSize_RelativeChain10()
    {
        float total = 0;
        for (int i = 0; i < 100; i++)
            total += relativeLeaf.DrawSize.X;
        return total;
    }

    /// <summary>
    /// Changes the innermost box of five nested auto-sizing containers, then reads the outer size.
    /// </summary>
    [Benchmark]
    public float ReadAutoSize_AfterLeafChange_Nested5()
    {
        flip = !flip;
        nestedLeaf.Width = flip ? 60 : 50;
        return nestedOuter.Size.X;
    }

    /// <summary>
    /// A frame of the nested chain after its leaf changed.
    /// </summary>
    [Benchmark]
    public void Frame_Nested5_LeafChanges()
    {
        flip = !flip;
        nestedLeaf.Width = flip ? 60 : 50;
        nestedClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        nestedRoot.UpdateSubTree();
    }

    /// <summary>
    /// A frame of the settings-like page with one control resizing.
    /// </summary>
    [Benchmark]
    public void Frame_SettingsPage_OneChange()
    {
        flip = !flip;
        uiChangingBox.Width = flip ? 170 : 160;
        uiClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        uiRoot.UpdateSubTree();
    }

    /// <summary>
    /// A frame of the settings-like page with nothing changing.
    /// </summary>
    [Benchmark]
    public void Frame_SettingsPage_Clean()
    {
        uiClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        uiRoot.UpdateSubTree();
    }

    /// <summary>
    /// A frame of an auto-sizing container around 50 anchored children, one of which resizes.
    /// </summary>
    [Benchmark]
    public void Frame_AnchoredAutoSize50_OneChange()
    {
        flip = !flip;
        anchoredChangingBox.Width = flip ? 40 : 35;
        anchoredClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        anchoredRoot.UpdateSubTree();
    }
}
