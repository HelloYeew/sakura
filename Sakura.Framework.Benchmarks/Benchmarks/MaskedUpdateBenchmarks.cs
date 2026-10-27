// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// The same trees with and without a masking container above them. One op is one frame.
/// </summary>
[MemoryDiagnoser]
public class MaskedUpdateBenchmarks
{
    [Params(false, true)]
    public bool Masked;

    private Container notesRoot = null!;
    private ManualClock notesClock = null!;
    private readonly List<Box> notes = new List<Box>();

    private Container moverRoot = null!;
    private ManualClock moverClock = null!;

    private Container staticRoot = null!;
    private ManualClock staticClock = null!;

    private Container clippedRoot = null!;
    private ManualClock clippedClock = null!;

    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        // 4 lanes x 64 notes whose positions are set before each frame (RhythmGameFrameBenchmarks' shape).
        (notesRoot, notesClock) = BenchmarkTree.CreateRoot();
        notes.Clear();

        var playfield = new Container { Position = new Vector2(440, 0), Size = new Vector2(400, 720), Masking = Masked };

        for (int i = 0; i < 4; i++)
        {
            var lane = new Container { Position = new Vector2(i * 100, 0), Size = new Vector2(100, 720) };

            for (int j = 0; j < 64; j++)
            {
                var note = new Box { Position = new Vector2(5, j * (720f / 64)), Size = new Vector2(90, 30) };
                lane.Add(note);
                notes.Add(note);
            }

            playfield.Add(lane);
        }

        notesRoot.Add(playfield);
        BenchmarkTree.LoadAndSettle(notesRoot, notesClock);

        // 1000 static boxes inside a parent that moves every frame (a scrolling or sliding panel).
        (moverRoot, moverClock) = BenchmarkTree.CreateRoot();
        var mask = new Container { Size = new Vector2(1280, 720), Masking = Masked };
        moverRoot.Add(mask);
        var holder = BenchmarkTree.AddWide(mask, 1000);
        BenchmarkTree.LoadAndSettle(moverRoot, moverClock);
        holder.MoveTo(new Vector2(0, 100), 1_000_000_000);

        // Control: the same 1000 boxes with nothing moving.
        (staticRoot, staticClock) = BenchmarkTree.CreateRoot();
        var staticMask = new Container { Size = new Vector2(1280, 720), Masking = Masked };
        staticRoot.Add(staticMask);
        BenchmarkTree.AddWide(staticMask, 1000);
        BenchmarkTree.LoadAndSettle(staticRoot, staticClock);

        // The same 1000 static boxes behind a mask 40 px tall: 128 inside it, 872 masked away (a list scrolled to its top).
        (clippedRoot, clippedClock) = BenchmarkTree.CreateRoot();
        var clip = new Container { Size = new Vector2(1280, 40), Masking = Masked };
        clippedRoot.Add(clip);
        BenchmarkTree.AddWide(clip, 1000);
        BenchmarkTree.LoadAndSettle(clippedRoot, clippedClock);
    }

    [Benchmark]
    public DrawNode ThousandStaticBoxesMostOutsideMask()
    {
        frame++;
        clippedClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        clippedRoot.UpdateSubTree();
        return clippedRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    [Benchmark]
    public DrawNode NotesSetBeforeFrame()
    {
        frame++;

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            float y = note.Position.Y + 6f;
            if (y > 720)
                y = -30;
            note.Position = new Vector2(note.Position.X, y);
        }

        notesClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        notesRoot.UpdateSubTree();
        return notesRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    [Benchmark]
    public DrawNode ThousandBoxesInMovingParent()
    {
        frame++;
        moverClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        moverRoot.UpdateSubTree();
        return moverRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    [Benchmark]
    public DrawNode ThousandStaticBoxes()
    {
        frame++;
        staticClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        staticRoot.UpdateSubTree();
        return staticRoot.GenerateDrawNodeSubtree(frame % 3);
    }
}
