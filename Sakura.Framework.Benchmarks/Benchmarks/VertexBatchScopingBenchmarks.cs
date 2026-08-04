// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using Vertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Benchmark for vertex structural change (shared vertex batches instead of per-drawable vertex arrays)
/// Test based on yuuki. song selection screen that this "should"(?) make the performance better
/// the number came from dotMemory snapshot
/// </summary>
[MemoryDiagnoser]
public class VertexBatchScopingBenchmarks
{
    /// <summary>
    /// Vertices per glyph, matching <c>SpriteText.computeLayout</c>.
    /// </summary>
    private const int vertices_per_glyph = 4;

    /// <summary>
    /// A drawable that owns a variable-length vertex array the way <see cref="SpriteText"/> does, with a
    /// draw node that copies it grow-only. Stands in for a shaped label of a given glyph count.
    /// </summary>
    private sealed partial class GlyphRun : Drawable
    {
        private int glyphCount;

        public GlyphRun(int glyphCount)
        {
            SetGlyphCount(glyphCount);
        }

        private int vertexCount => glyphCount * vertices_per_glyph;

        public void SetGlyphCount(int value)
        {
            glyphCount = value;

            if (Vertices.Length < vertexCount)
                Vertices = new Vertex[vertexCount];
        }

        protected override DrawNode CreateDrawNode() => new GlyphRunDrawNode();

        private sealed class GlyphRunDrawNode : DrawNode
        {
            protected override void ApplyVertices(Drawable source)
            {
                var run = (GlyphRun)source;

                // Grow-only, exactly as SpriteTextDrawNode.ApplyVertices is. The base class now owns the
                // grow-only storage; this override still exists because the source buffer is itself
                // grow-only, so only the live range may be copied rather than the whole array.
                SetVertexCount(run.vertexCount);
                run.Vertices.AsSpan(0, run.vertexCount).CopyTo(WritableVertices);
            }
        }
    }

    private Container root = null!;
    private ManualClock clock = null!;

    private GlyphRun boxSized = null!;
    private GlyphRun label = null!;
    private GlyphRun title = null!;

    private DrawNode boxNode = null!;
    private DrawNode labelNode = null!;
    private DrawNode titleNode = null!;

    private Container textHeavyRoot = null!;
    private ManualClock textHeavyClock = null!;
    private Container textHeavyHolder = null!;

    private int frame;

    /// <summary>
    /// A 30-character label and an 80-character title, which is what a song-select row carries.
    /// </summary>
    private const int label_glyphs = 30;

    private const int title_glyphs = 80;

    /// <summary>
    /// Rows on screen in a scrolling carousel, each with a title and an artist line.
    /// </summary>
    private const int rows_on_screen = 60;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();

        boxSized = new GlyphRun(1) { Size = new Vector2(16, 16) };
        label = new GlyphRun(label_glyphs) { Size = new Vector2(200, 20) };
        title = new GlyphRun(title_glyphs) { Size = new Vector2(600, 24) };

        root.Add(boxSized);
        root.Add(label);
        root.Add(title);
        BenchmarkTree.LoadAndSettle(root, clock);

        boxNode = boxSized.GenerateDrawNode(0);
        labelNode = label.GenerateDrawNode(0);
        titleNode = title.GenerateDrawNode(0);

        (textHeavyRoot, textHeavyClock) = BenchmarkTree.CreateRoot();

        var holder = new Container { RelativeSizeAxes = Axes.Both };

        for (int i = 0; i < rows_on_screen; i++)
        {
            holder.Add(new GlyphRun(title_glyphs) { Position = new Vector2(0, i * 12), Size = new Vector2(600, 24) });
            holder.Add(new GlyphRun(label_glyphs) { Position = new Vector2(0, i * 12 + 6), Size = new Vector2(200, 20) });
        }

        textHeavyHolder = holder;
        textHeavyRoot.Add(holder);
        BenchmarkTree.LoadAndSettle(textHeavyRoot, textHeavyClock);
    }

    #region The per-frame copy — what a shared batch would replace with a direct write

    /// <summary>
    /// One drawable's vertex snapshot at the default 4 vertices. The floor.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void ApplyVertices_4() => boxNode.ApplyState(boxSized);

    /// <summary>
    /// A 30-glyph label: 120 vertices, 7.2 KB copied.
    /// </summary>
    [Benchmark]
    public void ApplyVertices_Label120() => labelNode.ApplyState(label);

    /// <summary>
    /// An 80-glyph title: 320 vertices, 19.2 KB copied, once per frame per title on screen.
    /// </summary>
    [Benchmark]
    public void ApplyVertices_Title320() => titleNode.ApplyState(title);

    #endregion

    #region Steady state vs. churn

    /// <summary>
    /// A full frame over a carousel-shaped tree that is not changing: 60 rows, two runs each. Expected to
    /// allocate nothing, since every array is already big enough — which is the point worth pinning before
    /// redesigning anything, because it means SF-21's win here is CPU and resident footprint rather than
    /// the GC churn the original profile attributed to it.
    /// </summary>
    [Benchmark]
    public DrawNode Frame_TextHeavy_SteadyState()
    {
        frame++;
        textHeavyClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        textHeavyRoot.UpdateSubTree();
        return textHeavyRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// The same tree while it scrolls. The holder moves every frame, so every row's transforms and vertices
    /// are recomputed and re-copied — which is what song select actually does, and the only state in which
    /// the per-frame copy cost above is paid at all.
    /// </summary>
    /// <remarks>
    /// Compare against <see cref="Frame_TextHeavy_SteadyState"/>: the difference between the two is the
    /// entire per-frame vertex cost SF-21 would remove, and the ratio is what says whether the
    /// architectural change is worth it.
    /// </remarks>
    [Benchmark]
    public DrawNode Frame_TextHeavy_Scrolling()
    {
        frame++;
        textHeavyHolder.Position = new Vector2(0, frame % 2 == 0 ? 0 : 1);

        textHeavyClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        textHeavyRoot.UpdateSubTree();
        return textHeavyRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// The case that does allocate: a row gets a longer string than the array it inherited, so both the
    /// source array and the draw node's copy are replaced. Includes constructing the row, so read the
    /// allocation against the arithmetic — two 320-vertex arrays is 38,400 bytes of the figure.
    /// </summary>
    [Benchmark]
    public void GrowVertices_LongerTextInARecycledRow()
    {
        // Start small so each iteration has something to grow into, then grow past it.
        var run = new GlyphRun(1);
        var node = run.GenerateDrawNode(0);

        run.SetGlyphCount(title_glyphs);
        node.ApplyState(run);
    }

    #endregion

    #region Resident footprint

    /// <summary>
    /// Builds and settles a whole song-select-shaped screen, so the reported allocation is the resident cost
    /// of one screen's worth of text geometry — these arrays are retained, not churned, so allocated and
    /// resident are the same figure here.
    /// </summary>
    /// <remarks>
    /// Read against <see cref="Construct_TextHeavyScreen_FourVertexRuns"/>, which builds the identical tree
    /// with 4-vertex runs. The difference is what the glyph vertices themselves cost, and therefore the
    /// ceiling on what a shared batch could give back — one buffer sized to the frame instead of four arrays
    /// per drawable.
    /// </remarks>
    [Benchmark]
    public Container Construct_TextHeavyScreen() => buildScreen(title_glyphs, label_glyphs);

    /// <summary>
    /// The same tree with every run at the default 4 vertices, isolating the fixed per-drawable cost from
    /// the glyph-count-dependent one.
    /// </summary>
    [Benchmark]
    public Container Construct_TextHeavyScreen_FourVertexRuns() => buildScreen(1, 1);

    private static Container buildScreen(int titleGlyphs, int labelGlyphs)
    {
        var (screenRoot, screenClock) = BenchmarkTree.CreateRoot();
        var holder = new Container { RelativeSizeAxes = Axes.Both };

        for (int i = 0; i < rows_on_screen; i++)
        {
            holder.Add(new GlyphRun(titleGlyphs) { Position = new Vector2(0, i * 12), Size = new Vector2(600, 24) });
            holder.Add(new GlyphRun(labelGlyphs) { Position = new Vector2(0, i * 12 + 6), Size = new Vector2(200, 20) });
        }

        screenRoot.Add(holder);
        BenchmarkTree.LoadAndSettle(screenRoot, screenClock);

        // Three frames so all three draw-node buffers exist, which is the steady state a running app is in.
        for (int f = 0; f < 3; f++)
        {
            screenClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
            screenRoot.UpdateSubTree();
            screenRoot.GenerateDrawNodeSubtree(f);
        }

        return screenRoot;
    }

    #endregion
}
