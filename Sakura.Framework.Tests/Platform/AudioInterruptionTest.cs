// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Audio.Headless;
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

    private sealed class InterruptingHost : HeadlessAppHost
    {
        public InterruptingHost()
            : base(nameof(AudioInterruptionTest))
        {
        }

        public void Interrupt(bool interrupted) => SetAudioInterrupted(interrupted);
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
