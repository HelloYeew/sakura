// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// Regression tests for the "cleared drawables keep getting drawn" bug
/// https://github.com/HelloYeew/sakura/pull/131
/// </summary>
[TestFixture]
public class ClearGhostDrawNodeTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock
        {
            CurrentTime = 1000
        };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.LoadComplete();
    }

    /// <summary>
    /// A drawable that invalidates itself every update
    /// </summary>
    private partial class AnimatedBox : Box
    {
        private int tick;

        public override void Update()
        {
            base.Update();
            // Small wrapped drift so the value genuinely changes each frame (a no-op assignment
            // would not invalidate) without ever leaving the parent's bounds.
            Position = new Vector2(10 + tick++ % 40, 10);
        }
    }

    /// <summary>
    /// Advances one update-thread frame in the same order as the real host: input/mutations first
    /// (<paramref name="inputPhase"/>), then the update traversal, then draw-node generation for the
    /// given buffer. Returns the freshly generated root draw node.
    /// </summary>
    private ContainerDrawNode step(int buffer, Action? inputPhase = null)
    {
        manual.CurrentTime += 16;
        inputPhase?.Invoke();
        root.UpdateSubTree();
        return (ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer);
    }

    /// <summary>
    /// Counts leaf (non-container) draw nodes reachable from <paramref name="node"/> i.e. how many
    /// concrete drawables would actually be drawn this frame. A ghost shows up as leftover leaves.
    /// </summary>
    private static int countDrawnLeaves(DrawNode node)
    {
        if (node is not ContainerDrawNode container)
            return 1;

        int total = 0;
        foreach (var child in container.Children)
            total += countDrawnLeaves(child);
        return total;
    }

    /// <summary>
    /// an active (animated) subtree is cleared during the
    /// input phase, and every buffer must stop drawing the removed drawables.
    /// </summary>
    [Test]
    public void TestClearingActiveSubtreeDuringInputStopsDrawingItInAllBuffers()
    {
        var outer = new Container { Size = new Vector2(600, 500) };
        var target = new Container { Size = new Vector2(400, 400) };

        for (int i = 0; i < 5; i++)
            target.Add(new Box { Position = new Vector2(20 + i * 10, 20), Size = new Vector2(30) });

        // The activity source that keeps the subtree-dirty flags set frame to frame.
        target.Add(new AnimatedBox { Size = new Vector2(30) });

        outer.Add(target);
        root.Add(outer);

        // Warm up so every buffer holds a populated, cached tree.
        for (int f = 0; f < 9; f++)
            step(f % 3);

        Assert.That(countDrawnLeaves(step(0)), Is.EqualTo(6), "Sanity: all six drawables should be drawn before the clear.");

        // Clear during the input phase — before the update traversal — exactly like a click on the
        // "Clear test scene" step.
        step(0, () => target.Clear());

        // Let every buffer be regenerated at least once after the clear.
        for (int f = 0; f < 9; f++)
            step(f % 3);

        Assert.Multiple(() =>
        {
            for (int buffer = 0; buffer < 3; buffer++)
                Assert.That(countDrawnLeaves(step(buffer)), Is.Zero, $"Buffer {buffer} still draws removed drawables (ghosting).");
        });
    }

    /// <summary>
    /// Stress version
    /// </summary>
    [Test]
    public void TestClearNeverGhostsAcrossManyRandomizedRepetitions()
    {
        const int repetitions = 5000;
        var rng = new Random(1234);

        for (int rep = 0; rep < repetitions; rep++)
        {
            // Fresh scene each repetition.
            root.Clear();

            int depth = 1 + rng.Next(3);          // 1..3 nested containers above the target
            int boxCount = rng.Next(6);           // 0..5 static boxes in the target
            int warmup = 3 + rng.Next(9);         // frames to prime the buffers
            int settle = 6 + rng.Next(9);         // frames to let all buffers regenerate after clear
            int startBuffer = rng.Next(3);

            Container parent = root;
            for (int d = 0; d < depth; d++)
            {
                var c = new Container { Size = new Vector2(500 - d * 20, 480 - d * 20) };
                parent.Add(c);
                parent = c;
            }

            var target = parent;

            for (int i = 0; i < boxCount; i++)
                target.Add(new Box { Position = new Vector2(15 + i * 8, 15), Size = new Vector2(25) });

            // Keep the flags hot so the pre-update clear hits the worst-case dedup state.
            target.Add(new AnimatedBox { Size = new Vector2(25) });

            for (int f = 0; f < warmup; f++)
                step((startBuffer + f) % 3);

            // Clear in the input phase.
            step(startBuffer, () => target.Clear());

            for (int f = 0; f < settle; f++)
                step((startBuffer + f) % 3);

            for (int buffer = 0; buffer < 3; buffer++)
            {
                int drawn = countDrawnLeaves(step(buffer));
                Assert.That(drawn, Is.Zero,
                    $"Repetition {rep} (depth={depth}, boxes={boxCount}, warmup={warmup}, settle={settle}, startBuffer={startBuffer}): "
                    + $"buffer {buffer} still draws {drawn} removed drawable(s).");
            }
        }
    }
}
