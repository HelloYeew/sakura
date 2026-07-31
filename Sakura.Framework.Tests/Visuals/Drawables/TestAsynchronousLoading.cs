// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public partial class TestAsynchronousLoading : TestScene
{
    [SetUp]
    public void SetUp()
    {
        AddStep("Clear scene", () => Clear());
    }

    [Test]
    public void TestStandardAsyncLoadWithCallback()
    {
        AsyncBox? box = null;
        bool callbackFired = false;

        AddStep("Begin async load", () =>
        {
            box = new AsyncBox
            {
                Size = new Vector2(100),
                Position = new Vector2(50),
                Color = Color.LimeGreen
            };

            LoadComponentAsync(box, loadedBox =>
            {
                Add(loadedBox);
                callbackFired = true;
            });
        });

        AddUntilStep("Wait for callback", () => callbackFired);

        AddAssert("Box is loaded", () => box!.IsLoaded);
        AddAssert("Box LoadState is Loaded", () => box!.LoadState == LoadState.Loaded);
        AddAssert("Box is in hierarchy", () => Children.Contains(box!));
    }

    [Test]
    public void TestLongRunningLoadThrowsOnSync()
    {
        LongRunningBox? box = null;

        AddStep("Create long-running component", () =>
        {
            box = new LongRunningBox
            {
                Size = new Vector2(100),
                Position = new Vector2(200, 50),
                Color = Color.Red
            };
        });

        AddStep("Attempt synchronous add (Should Throw)", () =>
        {
            try
            {
                Add(box!);
                Assert.Fail("Expected InvalidOperationException to be thrown for synchronous load of a [LongRunningLoad] component.");
            }
            catch (InvalidOperationException)
            {
                // expected exception
            }
            catch (Exception ex)
            {
                Assert.Fail($"Threw the wrong exception type: {ex.GetType()}");
            }
        });

        AddAssert("Box remains NotLoaded", () => box!.LoadState == LoadState.NotLoaded);
        AddAssert("Box is not in hierarchy", () => !Children.Contains(box!));
    }

    [Test]
    public void TestLongRunningAsyncLoad()
    {
        LongRunningBox? box = null;
        Task? loadTask = null;

        AddStep("Begin async long-running load", () =>
        {
            box = new LongRunningBox
            {
                Size = new Vector2(100),
                Position = new Vector2(350, 50),
                Color = Color.Cyan
            };

            loadTask = LoadComponentAsync(box);
        });

        AddAssert("Box state is Loading or Ready", () => box!.LoadState >= LoadState.Loading);

        AddUntilStep("Wait for task completion", () => loadTask != null && loadTask.IsCompleted);

        AddAssert("Box state is Ready", () => box!.LoadState == LoadState.Ready);

        AddStep("Add to scene", () => Add(box!));

        AddAssert("Box state is Loaded", () => box!.LoadState == LoadState.Loaded);
        AddAssert("Box is in hierarchy", () => Children.Contains(box!));
    }

    /// <summary>
    /// A load that finished after its own cancellation has already allocated whatever it allocates, and
    /// the caller never hears about it — so without a discard path nothing ever releases it. This is the
    /// shape of the permanently-leaked cover texture: a cancelled cover load never reached the code that
    /// would have released it.
    /// </summary>
    [Test]
    public void TestCancelledLoadDisposesTheComponent()
    {
        GatedBox? box = null;
        var cts = new CancellationTokenSource();
        bool onLoadedFired = false;

        AddStep("Begin async load", () =>
        {
            box = new GatedBox { Size = new Vector2(100), Color = Color.Orange };
            LoadComponentAsync(box, _ => onLoadedFired = true, cts.Token);
        });

        // Cancelling only once the load body is running is what makes this deterministic: cancelling
        // earlier would stop the load before it started, which allocates nothing and discards nothing.
        AddUntilStep("Wait for load to start", () => box!.LoadStarted.IsSet);
        AddStep("Cancel, then let the load finish", () =>
        {
            cts.Cancel();
            box!.AllowFinish.Set();
        });

        AddUntilStep("Component was disposed", () => box!.IsDisposed);
        AddAssert("onLoaded never fired", () => !onLoadedFired);
        AddStep("Dispose token source", () => cts.Dispose());
    }

    [Test]
    public void TestLoadCancelledByItsOwnersDisposalStillDiscards()
    {
        OwningContainer? owner = null;
        GatedBox? box = null;

        AddStep("Add an owner and start a load from it", () =>
        {
            Add(owner = new OwningContainer());
            box = new GatedBox { Size = new Vector2(100), Color = Color.Orange };
            owner.StartLoad(box);
        });

        AddUntilStep("Wait for load to start", () => box!.LoadStarted.IsSet);

        // Removal disposes the owner, which cancels the load and clears the scheduler a discard would
        // otherwise have been routed through.
        AddStep("Tear the owner down", () => Remove(owner!));
        AddUntilStep("Owner is disposed", () => owner!.IsDisposed);

        // the load must finish after the cancellation, which is the whole scenario. letting it
        // finish first would race, and an uncanceled load with no onLoaded callback discards nothing.
        AddStep("Let the load finish", () => box!.AllowFinish.Set());

        AddUntilStep("The orphaned component was still released", () => box!.IsDisposed);
    }

    /// <summary>
    /// Supplying a discard handler takes the decision over: the framework hands the component to it and
    /// disposes nothing itself.
    /// </summary>
    [Test]
    public void TestCancelledLoadInvokesDiscardHandler()
    {
        GatedBox? box = null;
        var cts = new CancellationTokenSource();
        bool discarded = false;

        AddStep("Begin async load with discard handler", () =>
        {
            box = new GatedBox { Size = new Vector2(100), Color = Color.Orange };
            LoadComponentAsync(box, null, cts.Token, _ => discarded = true);
        });

        AddUntilStep("Wait for load to start", () => box!.LoadStarted.IsSet);
        AddStep("Cancel, then let the load finish", () =>
        {
            cts.Cancel();
            box!.AllowFinish.Set();
        });

        AddUntilStep("Discard handler fired", () => discarded);
        AddAssert("Framework left disposal to the handler", () => !box!.IsDisposed);
        AddStep("Dispose token source", () => cts.Dispose());
    }

    private class AsyncBox : Box
    {
        public override void Load()
        {
            base.Load();
            Thread.Sleep(50);
        }
    }

    private partial class OwningContainer : Container
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        public void StartLoad(Drawable component) => LoadComponentAsync(component, null, cancellation.Token);

        protected override void Dispose(bool isDisposing)
        {
            cancellation.Cancel();

            base.Dispose(isDisposing);
        }
    }

    /// <summary>
    /// A component whose load can be held open, so a test can cancel at a known point inside it.
    /// </summary>
    private class GatedBox : Box
    {
        public readonly ManualResetEventSlim LoadStarted = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim AllowFinish = new ManualResetEventSlim(false);

        public override void Load()
        {
            LoadStarted.Set();
            AllowFinish.Wait(5000);
            base.Load();
        }
    }

    [LongRunningLoad]
    private class LongRunningBox : Box
    {
        public override void Load()
        {
            base.Load();
            Thread.Sleep(150);
        }
    }
}
