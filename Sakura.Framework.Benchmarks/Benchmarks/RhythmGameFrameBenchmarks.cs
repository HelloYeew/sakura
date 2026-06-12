// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// End-to-end simulation of a rhythm game's per-frame work on the update thread:
/// scrolling notes in lanes, a static HUD, and (optionally) note spawn/despawn churn.
/// Each benchmark op is exactly one frame: advance clock → mutate → UpdateSubTree →
/// GenerateDrawNodeSubtree, mirroring <c>AppHost.PerformUpdate</c>.
/// </summary>
[MemoryDiagnoser]
public class RhythmGameFrameBenchmarks
{
    private const int lane_count = 4;

    [Params(16, 64)]
    public int NotesPerLane;

    private Container root = null!;
    private ManualClock clock = null!;
    private readonly List<Container> lanes = new List<Container>();
    private readonly List<Box> notes = new List<Box>();
    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        (root, clock) = BenchmarkTree.CreateRoot();
        lanes.Clear();
        notes.Clear();

        var playfield = new Container
        {
            Position = new Vector2(440, 0),
            Size = new Vector2(400, 720),
        };

        for (int i = 0; i < lane_count; i++)
        {
            var lane = new Container
            {
                Position = new Vector2(i * 100, 0),
                Size = new Vector2(100, 720),
            };

            for (int j = 0; j < NotesPerLane; j++)
            {
                var note = new Box
                {
                    Position = new Vector2(5, j * (720f / NotesPerLane)),
                    Size = new Vector2(90, 30),
                };
                lane.Add(note);
                notes.Add(note);
            }

            lanes.Add(lane);
            playfield.Add(lane);
        }

        root.Add(playfield);

        // Static HUD elements (score panel, judgement area, decorations).
        var hud = new Container { Size = new Vector2(1280, 720) };
        for (int i = 0; i < 20; i++)
        {
            hud.Add(new Box
            {
                Position = new Vector2(i * 60, 10),
                Size = new Vector2(50, 20),
            });
        }
        root.Add(hud);

        BenchmarkTree.LoadAndSettle(root, clock);
    }

    /// <summary>
    /// Gameplay steady state: every note scrolls down every frame, wrapping back to the top.
    /// This is the number that must stay well under the frame budget.
    /// </summary>
    [Benchmark(Baseline = true)]
    public DrawNode ScrollingNotesFrame()
    {
        frame++;
        const float scroll_speed = 6f; // px per frame

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            float y = note.Position.Y + scroll_speed;
            if (y > 720)
                y = -30;
            note.Position = new Vector2(note.Position.X, y);
        }

        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
        return root.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// Scrolling plus per-frame note churn: one note removed and one added per lane,
    /// simulating notes being hit/missed and new ones spawning. Exposes Add/Remove,
    /// Load and topology-invalidation cost under steady gameplay.
    /// </summary>
    [Benchmark]
    public DrawNode ScrollingWithSpawnDespawnFrame()
    {
        frame++;
        const float scroll_speed = 6f;

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            float y = note.Position.Y + scroll_speed;
            if (y > 720)
                y = -30;
            note.Position = new Vector2(note.Position.X, y);
        }

        // Despawn the first note of each lane, spawn a replacement at the top.
        for (int i = 0; i < lane_count; i++)
        {
            int noteIndex = i * NotesPerLane;
            var old = notes[noteIndex];
            lanes[i].Remove(old);

            var fresh = new Box
            {
                Position = new Vector2(5, -30),
                Size = new Vector2(90, 30),
            };
            lanes[i].Add(fresh);
            notes[noteIndex] = fresh;
        }

        clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        root.UpdateSubTree();
        return root.GenerateDrawNodeSubtree(frame % 3);
    }
}
