// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class LowMemoryTest
{
    /// <summary>
    /// The system says it's short of memory on a thread of its own (Android's UI thread); an app's caches live on the
    /// update thread, so that's where it hears of it.
    /// </summary>
    [Test]
    public void TheAppHearsOfItOnTheUpdateThread()
    {
        using var host = new HeadlessAppHost(nameof(TheAppHearsOfItOnTheUpdateThread));
        var app = new ShortOfMemoryApp(host);

        host.Run(app);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.HeardOn, Is.Not.Null, "the app wasn't told");
            Assert.That(app.HeardOn, Is.EqualTo(app.UpdateThread), "told on another thread than its updates'");
        }
    }

    private partial class ShortOfMemoryApp : App
    {
        private const int max_frames = 100;

        private readonly AppHost host;
        private int frames;

        public int? UpdateThread { get; private set; }
        public int? HeardOn { get; private set; }

        protected override Assembly ResourceAssembly => typeof(LowMemoryTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public ShortOfMemoryApp(AppHost host)
        {
            this.host = host;
        }

        public override void Update()
        {
            base.Update();

            if (UpdateThread == null)
            {
                UpdateThread = Environment.CurrentManagedThreadId;
                host.LowMemory += () => HeardOn = Environment.CurrentManagedThreadId;

                // From a thread of its own, as the system would.
                var system = new System.Threading.Thread(() => ((HeadlessWindow)host.Window).RaiseLowMemory());
                system.Start();
                system.Join();
                return;
            }

            if (HeardOn != null || ++frames == max_frames)
                host.Exit();
        }
    }
}
