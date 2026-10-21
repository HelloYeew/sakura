// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class EntryAssemblyTest
{
    /// <summary>
    /// A process started from Java (Android) has no managed entry point. The host has to run anyway,
    /// with the app's assembly standing in, rather than dying before the logger exists.
    /// </summary>
    [Test]
    public void HostWithoutAnEntryAssemblyUsesTheApps()
    {
        var original = RuntimeInfo.EntryAssembly;

        try
        {
            RuntimeInfo.EntryAssembly = null!;

            using var host = new HeadlessAppHost(nameof(HostWithoutAnEntryAssemblyUsesTheApps));
            var app = new ExitingApp(host);
            host.Run(app);

            Assert.That(app.Updated, Is.True, "the app ran");
            Assert.That(RuntimeInfo.EntryAssembly, Is.EqualTo(typeof(ExitingApp).Assembly));
        }
        finally
        {
            RuntimeInfo.EntryAssembly = original;
        }
    }

    private partial class ExitingApp : App
    {
        private readonly AppHost host;

        public bool Updated { get; private set; }

        protected override Assembly ResourceAssembly => typeof(EntryAssemblyTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public ExitingApp(AppHost host)
        {
            this.host = host;
        }

        public override void Update()
        {
            base.Update();

            if (Updated)
                return;

            Updated = true;
            host.Exit();
        }
    }
}
