// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Reactive;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Audio;

/// <summary>
/// Picks the output device the way a game's settings would, through <see cref="FrameworkSetting.AudioDevice"/>, with a
/// track playing to hear it move.
/// </summary>
[TestFixture]
[VisualTestOnly("Needs a real audio device to hear")]
public partial class TestOutputDevice : TestScene
{
    [Resolved]
    private IAudioManager audioManager { get; set; } = null!;

    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; } = null!;

    [Resolved]
    private FrameworkConfigManager frameworkConfigManager { get; set; } = null!;

    private Reactive<string> setting = null!;
    private BasicDropdown<DeviceChoice> dropdown = null!;
    private IAudioChannel? channel;

    [Test]
    public void TestPickingADevice()
    {
        AddStep("Show the devices", () =>
        {
            setting = frameworkConfigManager.Get<string>(FrameworkSetting.AudioDevice);

            Add(dropdown = new BasicDropdown<DeviceChoice>
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Width = 400,
                Margin = new MarginPadding(20)
            });

            listDevices();
            dropdown.Current.ValueChanged += e => setting.Value = e.NewValue.Name;
            audioManager.OutputDevicesChanged += listDevices;
        });

        AddStep("Play a track", () =>
        {
            channel = trackStore.Get("test.mp3").GetChannel();
            channel.Looping = true;
            channel.Play();
        });

        AddStep("Pick the system default", () => setting.Value = string.Empty);
    }

    [TearDown]
    public void TearDown() => AddStep("Stop", () =>
    {
        channel?.Stop();
        audioManager.OutputDevicesChanged -= listDevices;
    });

    private void listDevices()
    {
        dropdown.Items = audioManager.OutputDevices.Select(name => new DeviceChoice(name)).Prepend(new DeviceChoice(string.Empty)).ToArray();
        dropdown.Current.Value = new DeviceChoice(setting.Value);
    }

    private sealed record DeviceChoice(string Name)
    {
        public override string ToString() => Name.Length == 0 ? "System default" : Name;
    }
}
