// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class GameStateTest
{
    /// <summary>
    /// Starting up is a game's first loading screen; once its first frame is up, it isn't loading any more.
    /// </summary>
    [Test]
    public void AGameLoadsThroughStartup()
    {
        var states = run(AppType.Game);

        Assert.That(states, Is.EqualTo(new[] { (GameState.None, true), (GameState.None, false) }));
    }

    [Test]
    public void AnApplicationSaysNothing()
    {
        Assert.That(run(AppType.Application), Is.Empty);
    }

    private static (GameState State, bool Loading)[] run(AppType appType)
    {
        var performance = new RecordingPerformance();

        using (var host = new RecordingHost(appType, performance))
            host.Run(new ExitingApp(host));

        return performance.States.ToArray();
    }

    private sealed class RecordingPerformance : IPlatformPerformance
    {
        public readonly ConcurrentQueue<(GameState, bool)> States = new ConcurrentQueue<(GameState, bool)>();

        public void SetGameState(GameState state, bool loading) => States.Enqueue((state, loading));
    }

    private sealed class RecordingHost : HeadlessAppHost
    {
        public RecordingHost(AppType appType, IPlatformPerformance performance)
            : base($"{nameof(GameStateTest)}{appType}", new HostOptions { AppType = appType })
        {
            Performance = performance;
        }

        public override IPlatformPerformance Performance { get; }
    }

    private partial class ExitingApp : App
    {
        private readonly AppHost host;

        protected override Assembly ResourceAssembly => typeof(GameStateTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public ExitingApp(AppHost host)
        {
            this.host = host;
        }

        public override void Update()
        {
            base.Update();
            host.Exit();
        }
    }
}
