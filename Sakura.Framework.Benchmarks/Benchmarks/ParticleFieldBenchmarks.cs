// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// A storm of pieces, each moved, turned and faded every frame: set again on a <see cref="ParticleField"/>, as whatever moves
/// its pieces does, against one <see cref="Box"/> for each piece. Also a field with nothing changing, and one only fading.
/// One op is one frame on the update thread, its draw nodes included; what they then cost to draw isn't.
/// </summary>
[MemoryDiagnoser]
public class ParticleFieldBenchmarks
{
    private const float area_width = 1280;
    private const float area_height = 720;

    private static readonly Color[] tints =
    {
        ColorExtensions.FromHex("#FFB7D5"),
        ColorExtensions.FromHex("#FFE3EE"),
        ColorExtensions.FromHex("#C9A7F0"),
        ColorExtensions.FromHex("#FFD27F")
    };

    [Params(100, 500, 2000)]
    public int Pieces;

    private Vector2[] starts = null!;

    private Container boxRoot = null!;
    private ManualClock boxClock = null!;
    private Box[] boxes = null!;

    private Container movingRoot = null!;
    private ManualClock movingClock = null!;
    private ParticleField moving = null!;

    private Container standingRoot = null!;
    private ManualClock standingClock = null!;

    private Container fadingRoot = null!;
    private ManualClock fadingClock = null!;
    private ParticleField fading = null!;

    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(1);

        starts = new Vector2[Pieces];
        boxes = new Box[Pieces];

        for (int i = 0; i < Pieces; i++)
        {
            starts[i] = new Vector2((float)random.NextDouble() * area_width, (float)random.NextDouble() * area_height);
            boxes[i] = new Box { Size = new Vector2(24, 12), Color = tints[i % tints.Length] };
        }

        (boxRoot, boxClock) = BenchmarkTree.CreateRoot();
        boxRoot.AddRange(boxes);

        (movingRoot, movingClock) = BenchmarkTree.CreateRoot();
        movingRoot.Add(moving = new ParticleField());

        (standingRoot, standingClock) = BenchmarkTree.CreateRoot();
        ParticleField standing;
        standingRoot.Add(standing = new ParticleField());

        (fadingRoot, fadingClock) = BenchmarkTree.CreateRoot();
        fadingRoot.Add(fading = new ParticleField());

        for (int i = 0; i < Pieces; i++)
        {
            standing.Add(pieceAt(i));
            fading.Add(pieceAt(i));
        }

        foreach (Container root in new[] { boxRoot, movingRoot, standingRoot, fadingRoot })
        {
            root.Load();
            root.CompleteLoad();
            root.UpdateSubTree();
        }
    }

    private Particle pieceAt(int i)
    {
        (Vector2 position, float rotation, float alpha) = motionOf(i);
        return new Particle(position, new Vector2(24, 12), rotation, tints[i % tints.Length], alpha);
    }

    // Where a piece is this frame: drifting, turning and twinkling, each at its own rate.
    private (Vector2 Position, float Rotation, float Alpha) motionOf(int i)
    {
        float t = frame + i;
        return (starts[i] + new Vector2(MathF.Sin(t * 0.02f) * 30, t * 0.3f % area_height), t * 0.03f, 0.5f + 0.5f * MathF.Sin(t * 0.05f));
    }

    private static DrawNode finishFrame(Container root, ManualClock clock, int frame)
    {
        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
        return root.GenerateDrawNodeSubtree(frame % 3);
    }

    [Benchmark(Baseline = true)]
    public DrawNode OneBoxForEachPiece()
    {
        frame++;

        for (int i = 0; i < boxes.Length; i++)
        {
            (Vector2 position, float rotation, float alpha) = motionOf(i);

            Box box = boxes[i];
            box.Position = position;
            box.Rotation = rotation * 180 / MathF.PI;
            box.Alpha = alpha;
        }

        return finishFrame(boxRoot, boxClock, frame);
    }

    [Benchmark]
    public DrawNode FieldSetEveryFrame()
    {
        frame++;
        moving.Clear();

        for (int i = 0; i < Pieces; i++)
            moving.Add(pieceAt(i));

        return finishFrame(movingRoot, movingClock, frame);
    }

    /// <summary>
    /// Nothing changing: what a field costs while it just sits there.
    /// </summary>
    [Benchmark]
    public DrawNode FieldStanding()
    {
        frame++;
        return finishFrame(standingRoot, standingClock, frame);
    }

    /// <summary>
    /// Its pieces where they are, the whole field fading: its vertices are scaled, not built.
    /// </summary>
    [Benchmark]
    public DrawNode FieldFading()
    {
        frame++;
        fading.Alpha = 0.25f + 0.75f * (MathF.Sin(frame * 0.05f) * 0.5f + 0.5f);
        return finishFrame(fadingRoot, fadingClock, frame);
    }
}
