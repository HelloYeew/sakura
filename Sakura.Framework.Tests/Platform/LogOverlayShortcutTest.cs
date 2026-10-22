// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Configurations;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class LogOverlayShortcutTest
{
    [Test]
    public void CtrlShiftLTogglesTheLogOverlay()
    {
        using var host = new HeadlessAppHost(nameof(CtrlShiftLTogglesTheLogOverlay));
        var app = new PressingApp(host);

        host.Run(app);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.Before, Is.False, "the overlay starts off");
            Assert.That(app.AfterFirst, Is.True, "Ctrl+Shift+L turned it on");
            Assert.That(app.AfterSecond, Is.False, "Ctrl+Shift+L again turned it off");
        }
    }

    private partial class PressingApp : App
    {
        private readonly AppHost host;

        public bool? Before { get; private set; }
        public bool? AfterFirst { get; private set; }
        public bool? AfterSecond { get; private set; }

        protected override Assembly ResourceAssembly => typeof(LogOverlayShortcutTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public PressingApp(AppHost host)
        {
            this.host = host;
        }

        public override void Update()
        {
            base.Update();

            var shown = host.FrameworkConfigManager.Get<bool>(FrameworkSetting.ShowLogOverlay);

            Before = shown.Value;
            press();
            AfterFirst = shown.Value;
            press();
            AfterSecond = shown.Value;

            host.Exit();
        }

        private void press()
        {
            var keyDown = new KeyEvent(Key.L, KeyModifiers.Control | KeyModifiers.Shift, false);
            OnKeyDown(keyDown);
            OnKeyUp(keyDown);
        }
    }
}
