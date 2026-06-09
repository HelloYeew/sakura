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
        AddStep("Clear scene", Clear);
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

    private class AsyncBox : Box
    {
        public override void Load()
        {
            base.Load();
            Thread.Sleep(50);
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
