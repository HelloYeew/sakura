// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Audio.Headless;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class AudioInterruptionTest
{
    /// <summary>
    /// What the platform says about the audio output reaches the app's audio manager, and a manager made while the
    /// output is taken starts out knowing it.
    /// </summary>
    [Test]
    public void TheAppHearsWhatThePlatformSays()
    {
        var app = new InterruptedApp();

        using (var host = new InterruptingHost())
            host.Run(app);

        Assert.That(app.Seen, Is.EqualTo(new[] { true, true, false }));
    }

    /// <summary>
    /// Only a press made while the output is taken tells the platform the player is back for the app's sound: one a
    /// press, not a release or a move, and nothing once the output is back.
    /// </summary>
    [Test]
    public void APressWhileTheOutputIsTakenAsksForItBack()
    {
        var app = new PressingApp();

        using (var host = new InterruptingHost())
            host.Run(app);

        Assert.That(app.Asked, Is.EqualTo(new[] { 0, 2, 2 }));
    }

    private sealed class InterruptingHost : HeadlessAppHost
    {
        public int PressesWhileInterrupted;

        public InterruptingHost()
            : base(nameof(AudioInterruptionTest))
        {
        }

        public void Interrupt(bool interrupted) => SetAudioInterrupted(interrupted);

        protected override void OnPressWhileAudioInterrupted() => PressesWhileInterrupted++;

        /// <summary>
        /// Raises one of the headless window's input events, as a real window does for the player's input.
        /// </summary>
        public void Raise<T>(string windowEvent, T e)
        {
            var handler = (Action<T>?)typeof(HeadlessWindow).GetField(windowEvent, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Window);
            handler!(e);
        }
    }

    private partial class PressingApp : App
    {
        public readonly List<int> Asked = new List<int>();

        private int frame;

        protected override Assembly ResourceAssembly => typeof(AudioInterruptionTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public override void Update()
        {
            base.Update();

            var host = (InterruptingHost)Host;

            switch (++frame)
            {
                case 1:
                    press(host);
                    break;

                case 3:
                    Asked.Add(host.PressesWhileInterrupted);
                    host.Interrupt(true);
                    break;

                case 5:
                    press(host);
                    break;

                case 7:
                    Asked.Add(host.PressesWhileInterrupted);
                    host.Interrupt(false);
                    break;

                case 9:
                    press(host);
                    break;

                case 11:
                    Asked.Add(host.PressesWhileInterrupted);
                    host.Exit();
                    break;
            }
        }

        // A key held long enough to repeat and a touch, each let go, and a finger moved: two presses.
        private static void press(InterruptingHost host)
        {
            host.Raise("OnKeyDown", new KeyEvent(Key.A, KeyModifiers.None, false));
            host.Raise("OnKeyDown", new KeyEvent(Key.A, KeyModifiers.None, true));
            host.Raise("OnKeyUp", new KeyEvent(Key.A, KeyModifiers.None, false));
            host.Raise("OnTouchDown", new TouchEvent(TouchSource.Touch1, new Vector2(10, 10)));
            host.Raise("OnTouchMove", new TouchEvent(TouchSource.Touch1, new Vector2(20, 10)));
            host.Raise("OnTouchUp", new TouchEvent(TouchSource.Touch1, new Vector2(20, 10)));
        }
    }

    private partial class InterruptedApp : App
    {
        public readonly List<bool> Seen = new List<bool>();

        private int frame;

        protected override Assembly ResourceAssembly => typeof(AudioInterruptionTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public override void Update()
        {
            base.Update();

            var host = (InterruptingHost)Host;

            switch (++frame)
            {
                case 1:
                    host.Interrupt(true);
                    break;

                case 3:
                    Seen.Add(AudioManager.OutputInterrupted.Value);

                    var later = new HeadlessAudioManager();
                    host.AudioManager = later;
                    Seen.Add(later.OutputInterrupted.Value);
                    host.AudioManager = AudioManager;

                    host.Interrupt(false);
                    break;

                case 5:
                    Seen.Add(AudioManager.OutputInterrupted.Value);
                    host.Exit();
                    break;
            }
        }
    }
}
