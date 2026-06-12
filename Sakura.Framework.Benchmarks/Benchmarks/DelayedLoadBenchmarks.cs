// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures <see cref="DelayedLoadWrapperContainer"/> / <see cref="DelayedLoadUnloadWrapperContainer"/>.
/// <remark>
/// The idle benchmarks capture the per-frame tax of having many placeholder wrappers in the
/// tree (e.g. a 500-panel song-select carousel) wrappers are <c>AlwaysPresent</c> so they
/// update every frame even while masked away — that cost must stay tiny.
/// The cycle benchmark captures the full churn of scrolling an item into view, async-loading
/// it, scrolling it away and unloading (allocation pressure matters as much as time here).
/// </remark>
/// </summary>
[MemoryDiagnoser]
public class DelayedLoadBenchmarks
{
    private const int idle_count = 500;
    private const int loaded_count = 200;

    private static readonly Vector2 wrapper_size = new Vector2(300, 80);
    private static readonly Vector2 inside_position = new Vector2(10, 10);
    private static readonly Vector2 outside_position = new Vector2(10, 5000);

    private Container offscreenRoot = null!;
    private ManualClock offscreenClock = null!;

    private Container loadedRoot = null!;
    private ManualClock loadedClock = null!;

    private Container cycleRoot = null!;
    private ManualClock cycleClock = null!;
    private DelayedLoadUnloadWrapperContainer cycleWrapperContainer = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 500 wrappers permanently off screen (placeholder tax)
        (offscreenRoot, offscreenClock) = BenchmarkTree.CreateRoot();
        var offscreenViewport = makeViewport();
        offscreenRoot.Add(offscreenViewport);

        for (int i = 0; i < idle_count; i++)
        {
            offscreenViewport.Add(new DelayedLoadWrapperContainer(static () => new Box { Size = wrapper_size }, 500)
            {
                Size = wrapper_size,
                Position = outside_position
            });
        }

        BenchmarkTree.LoadAndSettle(offscreenRoot, offscreenClock);

        // 200 wrappers on screen with loaded content (steady state after loading)
        (loadedRoot, loadedClock) = BenchmarkTree.CreateRoot();
        var loadedViewport = makeViewport();
        loadedRoot.Add(loadedViewport);

        var loadedWrappers = new DelayedLoadWrapperContainer[loaded_count];

        for (int i = 0; i < loaded_count; i++)
        {
            loadedViewport.Add(loadedWrappers[i] = new DelayedLoadWrapperContainer(static () => new Box
            {
                Size = wrapper_size
            }, 0)
            {
                Size = wrapper_size,
                Position = inside_position
            });
        }

        BenchmarkTree.LoadAndSettle(loadedRoot, loadedClock);
        stepUntil(loadedRoot, loadedClock, () =>
        {
            for (int i = 0; i < loadedWrappers.Length; i++)
            {
                if (!loadedWrappers[i].DelayedLoadCompleted)
                    return false;
            }

            return true;
        });

        // One wrapper cycled in/out of view per benchmark invocation.
        (cycleRoot, cycleClock) = BenchmarkTree.CreateRoot();
        var cycleViewport = makeViewport();
        cycleRoot.Add(cycleViewport);

        cycleViewport.Add(cycleWrapperContainer = new DelayedLoadUnloadWrapperContainer(static () => new Box
        {
            Size = wrapper_size
        }, 0, 0)
        {
            Size = wrapper_size,
            Position = outside_position
        });

        BenchmarkTree.LoadAndSettle(cycleRoot, cycleClock);
    }

    private static Container makeViewport() => new Container
    {
        Size = new Vector2(1280, 720),
        Masking = true
    };

    /// <summary>
    /// Steps frames until <paramref name="condition"/> holds (async loads need real time,
    /// so each step also yields the thread).
    /// </summary>
    private static void stepUntil(Container root, ManualClock clock, Func<bool> condition)
    {
        int frames = 0;

        while (!condition())
        {
            if (++frames > 1_000_000)
                throw new TimeoutException("Benchmark condition was not reached.");

            clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
            root.UpdateSubTree();
            Thread.Yield();
        }
    }

    /// <summary>
    /// One update frame over 500 off-screen placeholder wrappers — the carousel-at-rest tax.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void OffscreenWrappers500_UpdateFrame()
    {
        offscreenClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        offscreenRoot.UpdateSubTree();
    }

    /// <summary>
    /// One update frame over 200 wrappers with loaded content — the visible-page steady state.
    /// </summary>
    [Benchmark]
    public void LoadedWrappers200_UpdateFrame()
    {
        loadedClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        loadedRoot.UpdateSubTree();
    }

    /// <summary>
    /// A full scroll-past cycle: into view → async load completes → out of view → unload.
    /// Includes the async hand-off and content recreation, so watch Allocated as much as Mean.
    /// </summary>
    [Benchmark]
    public void LoadUnloadCycle()
    {
        cycleWrapperContainer.Position = inside_position;
        stepUntil(cycleRoot, cycleClock, () => cycleWrapperContainer.DelayedLoadCompleted);

        cycleWrapperContainer.Position = outside_position;
        stepUntil(cycleRoot, cycleClock, () => !cycleWrapperContainer.DelayedLoadCompleted);
    }
}
