// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Input;

/// <summary>
/// On a phone, fingers come in through the app's root, which does more for a press than a test's input manager:
/// it brings a debug window under the finger to the front and watches for the debug tools gesture.
/// </summary>
[TestFixture]
public class AppTouchTest
{
    [Test]
    public void FingersThroughTheAppDoNotAllocate()
    {
        using var host = new HeadlessAppHost(nameof(FingersThroughTheAppDoNotAllocate));
        var app = new FingerApp(host);

        host.Run(app);

        Assert.Multiple(() =>
        {
            Assert.That(app.Allocated, Is.Zero);
            Assert.That(app.Scrolled, Is.GreaterThan(0), "the flicks scrolled nothing");
            Assert.That(app.Clicks, Is.GreaterThan(0), "the taps clicked nothing");
        });
    }

    private partial class FingerApp : App
    {
        private readonly AppHost host;

        private ScrollableContainer scroll = null!;
        private ClickCounter button = null!;
        private int frames;
        private double time = 1000;

        public long? Allocated { get; private set; }
        public float Scrolled { get; private set; }
        public int Clicks { get; private set; }

        protected override Assembly ResourceAssembly => typeof(AppTouchTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public FingerApp(AppHost host)
        {
            this.host = host;
        }

        public override void Update()
        {
            base.Update();

            switch (++frames)
            {
                case 2:
                    Add(scroll = new ScrollableContainer
                    {
                        Size = new Vector2(200, 300),
                        Child = new Box { Size = new Vector2(200, 4000) }
                    });
                    Add(button = new ClickCounter { X = 400, Size = new Vector2(200) });
                    break;

                // Laid out by now. Once first: the emulated mouse's set of buttons grows on its first press, and a
                // scroll container sets up on its first drag.
                case 10:
                    gestures();

                    long before = GC.GetAllocatedBytesForCurrentThread();
                    gestures();
                    Allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    break;

                case 12:
                    Scrolled = scroll.CurrentScroll.Y;
                    Clicks = button.Clicks;
                    host.Exit();
                    break;
            }
        }

        private void gestures()
        {
            // A flick up the list.
            down(TouchSource.Touch1, new Vector2(100, 250));

            for (int i = 1; i <= 20; i++)
                move(TouchSource.Touch1, new Vector2(100, 250 - 5 * i), new Vector2(0, -5));

            up(TouchSource.Touch1, new Vector2(100, 150));

            // Taps, far enough apart not to be a double tap, and a drag over what the finger landed on.
            for (int i = 0; i < 3; i++)
            {
                time += 1000;
                down(TouchSource.Touch1, new Vector2(500, 100));
                up(TouchSource.Touch1, new Vector2(500, 100));
            }

            down(TouchSource.Touch1, new Vector2(450, 50));
            move(TouchSource.Touch1, new Vector2(550, 50), new Vector2(100, 0));
            up(TouchSource.Touch1, new Vector2(550, 50));

            // A second finger landing next to the first, as a pinch starts.
            down(TouchSource.Touch1, new Vector2(450, 100));
            down(TouchSource.Touch2, new Vector2(550, 100));
            move(TouchSource.Touch1, new Vector2(430, 100), new Vector2(-20, 0));
            move(TouchSource.Touch2, new Vector2(570, 100), new Vector2(20, 0));
            up(TouchSource.Touch1, new Vector2(430, 100));
            up(TouchSource.Touch2, new Vector2(570, 100));
        }

        private void down(TouchSource source, Vector2 position) => OnTouchDown(new TouchEvent(source, position, timestamp: time += 10));

        private void move(TouchSource source, Vector2 position, Vector2 delta) => OnTouchMove(new TouchEvent(source, position, delta, time += 10));

        private void up(TouchSource source, Vector2 position) => OnTouchUp(new TouchEvent(source, position, timestamp: time += 10));
    }

    private partial class ClickCounter : Box
    {
        public int Clicks;

        public override bool OnClick(MouseButtonEvent e)
        {
            Clicks++;
            return true;
        }
    }
}
