// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class KeepScreenOnTest
{
    [TestCase(AppType.Game, true)]
    [TestCase(AppType.Application, false)]
    public void TheWindowFollowsTheHost(AppType appType, bool startsOn)
    {
        using var host = new HeadlessAppHost(nameof(TheWindowFollowsTheHost), new HostOptions { AppType = appType });
        var app = new FlippingApp(host);

        host.Run(app);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.StartedOn, Is.EqualTo(startsOn), "the window started the way the app type asks");
            Assert.That(app.FlippedOn, Is.EqualTo(!startsOn), "the window followed the change");
        }
    }

    private partial class FlippingApp : App
    {
        private const int max_frames = 100;

        private readonly AppHost host;
        private int frames;

        public bool? StartedOn { get; private set; }
        public bool? FlippedOn { get; private set; }

        protected override Assembly ResourceAssembly => typeof(KeepScreenOnTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public FlippingApp(AppHost host)
        {
            this.host = host;
        }

        public override void Update()
        {
            base.Update();

            if (StartedOn == null)
            {
                StartedOn = host.Window.KeepScreenOn;
                host.KeepScreenOn.Value = !host.KeepScreenOn.Value;
                return;
            }

            // The window is changed on the main thread, a frame or so later.
            if (host.Window.KeepScreenOn != StartedOn || ++frames == max_frames)
            {
                FlippedOn = host.Window.KeepScreenOn;
                host.Exit();
            }
        }
    }
}
