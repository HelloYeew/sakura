// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks;

/// <summary>
/// Shared tree shapes and a frame driver that mirrors what <c>AppHost.PerformUpdate</c>
/// does per frame (update subtree, then generate the draw node subtree into a cycling buffer).
/// </summary>
public static class BenchmarkTree
{
    public const float FRAME_STEP_MS = 1000f / 120f;

    /// <summary>
    /// Creates a root container driven by a <see cref="ManualClock"/> so benchmarks
    /// control time deterministically (transforms, lifetimes, schedulers).
    /// </summary>
    public static (Container root, ManualClock clock) CreateRoot(float width = 1280, float height = 720)
    {
        // Logger.log blocks on a gate that is only opened by Initialize; the DI reflection
        // fallback logs once per type during Load, so this must run before any tree is loaded.
        // Initialize is idempotent, calling it per-setup is safe.
        Logger.Initialize();

        var manualClock = new ManualClock();
        var root = new Container
        {
            Size = new Vector2(width, height),
            Clock = new FramedClock(manualClock)
        };
        return (root, manualClock);
    }

    /// <summary>
    /// A single container with <paramref name="count"/> boxes. Stresses per-child bookkeeping
    /// (clock updates, scheduler updates, vertex generation) with minimal nesting.
    /// </summary>
    public static Container AddWide(Container root, int count)
    {
        var holder = new Container { Size = new Vector2(1, 1), RelativeSizeAxes = Axes.Both };

        for (int i = 0; i < count; i++)
        {
            holder.Add(new Box
            {
                Position = new Vector2(i % 64 * 20, i / 64 * 20),
                Size = new Vector2(16, 16),
            });
        }

        root.Add(holder);
        return holder;
    }

    /// <summary>
    /// <paramref name="containers"/> column containers, each with <paramref name="childrenPerContainer"/>
    /// boxes. The closest shape to real UI / playfield scenes.
    /// </summary>
    public static Container AddGrid(Container root, int containers, int childrenPerContainer)
    {
        var holder = new Container { Size = new Vector2(1, 1), RelativeSizeAxes = Axes.Both };

        for (int i = 0; i < containers; i++)
        {
            var column = new Container
            {
                Position = new Vector2(i * (1280f / containers), 0),
                Size = new Vector2(1280f / containers, 720),
            };

            for (int j = 0; j < childrenPerContainer; j++)
            {
                column.Add(new Box
                {
                    Position = new Vector2(0, j * (720f / childrenPerContainer)),
                    Size = new Vector2(1280f / containers, 720f / childrenPerContainer),
                });
            }

            holder.Add(column);
        }

        root.Add(holder);
        return holder;
    }

    /// <summary>
    /// A chain of <paramref name="depth"/> nested containers with a single box at the bottom.
    /// Stresses matrix concatenation and invalidation propagation depth.
    /// </summary>
    public static (Container top, Box leaf) AddDeep(Container root, int depth)
    {
        var top = new Container { Size = new Vector2(1280, 720) };
        Container current = top;

        for (int i = 0; i < depth - 1; i++)
        {
            var next = new Container
            {
                Position = new Vector2(1, 1),
                Size = new Vector2(1280 - i, 720 - i),
            };
            current.Add(next);
            current = next;
        }

        var leaf = new Box
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(16, 16),
        };
        current.Add(leaf);

        root.Add(top);
        return (top, leaf);
    }

    /// <summary>
    /// Loads the tree and runs a few settle frames so every drawable is in a clean,
    /// steady state before measurement starts.
    /// </summary>
    public static void LoadAndSettle(Container root, ManualClock clock, int settleFrames = 3)
    {
        root.Load();
        root.LoadComplete();

        for (int i = 0; i < settleFrames; i++)
        {
            clock.CurrentTime += FRAME_STEP_MS;
            root.UpdateSubTree();
            root.GenerateDrawNodeSubtree(i % 3);
        }
    }
}
