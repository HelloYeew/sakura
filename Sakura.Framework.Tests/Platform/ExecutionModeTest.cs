// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class ExecutionModeTest
{
    [Test]
    public void HeadlessHostRunsInTheRequestedExecutionMode()
    {
        var expected = FrameworkEnvironment.StartupExecutionMode ?? ExecutionMode.MultiThread;

        using var host = new HeadlessAppHost(nameof(HeadlessHostRunsInTheRequestedExecutionMode));
        var app = new ProbeApp(host);

        int runningThread = Environment.CurrentManagedThreadId;
        host.Run(app);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.ObservedMode, Is.EqualTo(expected), "the host's execution mode");
            
            if (expected == ExecutionMode.SingleThread)
                Assert.That(app.UpdateThread, Is.EqualTo(runningThread), "updates ran on the thread that called Run");
            else
                Assert.That(app.UpdateThread, Is.Not.EqualTo(runningThread), "updates ran on a thread of their own");
        }
    }

    private partial class ProbeApp : App
    {
        private readonly AppHost host;

        public ExecutionMode? ObservedMode { get; private set; }
        public int UpdateThread { get; private set; }

        protected override Assembly ResourceAssembly => typeof(ExecutionModeTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public ProbeApp(AppHost host)
        {
            this.host = host;
        }

        public override void Update()
        {
            base.Update();

            if (ObservedMode != null)
                return;

            ObservedMode = host.ExecutionMode.Value;
            UpdateThread = Environment.CurrentManagedThreadId;
            host.Exit();
        }
    }
}
