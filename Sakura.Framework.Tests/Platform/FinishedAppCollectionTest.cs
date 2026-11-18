// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// Tests that an app is collected once its host has finished, so a process that runs one app after another, as a test run
/// does with a headless app for every test, doesn't keep them all.
/// </summary>
/// <remarks>
/// Every strong reference to an app or a scene lives in a helper that has returned before the collection: a Debug build keeps
/// a method's locals and temporaries alive until it returns.
/// </remarks>
[TestFixture]
public class FinishedAppCollectionTest
{
    /// <summary>
    /// Finished apps are collected. Each one's log overlay used to stay subscribed to the logger's static event, and the
    /// logger kept the last host's storage, so every app stayed alive.
    /// </summary>
    [Test]
    public void FinishedAppsAreCollected()
    {
        var apps = runApps(3);

        collect();

        Assert.That(apps.Count(app => app.IsAlive), Is.Zero);
    }

    /// <summary>
    /// The app a test scene runs its steps in under NUnit is collected once the scene is dropped too.
    /// </summary>
    [Test]
    public void TestSceneAppsAreCollected()
    {
        var apps = runScenes(3);

        collect();

        Assert.That(apps.Count(app => app.IsAlive), Is.Zero);
    }

    /// <summary>
    /// Runs a full collection, finalizers included, until nothing more is freed.
    /// </summary>
    private static void collect()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Runs <paramref name="count"/> apps on headless hosts, one after another, each until it exits.
    /// </summary>
    /// <returns>A weak reference to each app.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] runApps(int count)
    {
        var apps = new WeakReference[count];

        for (int i = 0; i < count; i++)
            apps[i] = runApp();

        return apps;
    }

    /// <summary>
    /// Runs one app on a headless host until it exits, and disposes the host.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference runApp()
    {
        using var host = new HeadlessAppHost("finished-app-collection");
        var app = new ExitingApp();
        host.Run(app);
        return new WeakReference(app);
    }

    /// <summary>
    /// Runs <paramref name="count"/> test scenes, each with one step, the way NUnit runs them.
    /// </summary>
    /// <returns>A weak reference to the app each scene ran in.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] runScenes(int count)
    {
        var apps = new WeakReference[count];

        for (int i = 0; i < count; i++)
            apps[i] = runScene();

        return apps;
    }

    /// <summary>
    /// Runs one test scene with a step that records the app it runs in.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference runScene()
    {
        var scene = new RecordingScene();
        scene.SetupNUnit();
        scene.RecordApp();
        scene.RunQueuedSteps();

        Assert.That(scene.App.IsAlive, Is.True, "the step ran and recorded its app");
        return scene.App;
    }

    /// <summary>
    /// An app that exits after a few frames.
    /// </summary>
    private partial class ExitingApp : App
    {
        private int frames;

        public override void Update()
        {
            base.Update();

            if (++frames == 3)
                Host.Exit();
        }
    }

    /// <summary>
    /// A test scene with one step, which records the app it runs in.
    /// </summary>
    private partial class RecordingScene : TestScene
    {
        /// <summary>
        /// The app the step ran in, held weakly so the scene doesn't keep it.
        /// </summary>
        public WeakReference App { get; } = new WeakReference(null);

        /// <summary>
        /// Queues the step that records the app.
        /// </summary>
        public void RecordApp() => AddStep("Record the app", () => App.Target = Parent);
    }
}
