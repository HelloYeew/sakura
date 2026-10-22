// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Configurations;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Configurations;

public class FrameworkConfigManagerTest
{
    [TestCase(AppType.Game, FrameSync.Limit2x)]
    [TestCase(AppType.Application, FrameSync.VSync)]
    public void TestDefaultFrameLimiterFollowsAppType(AppType appType, FrameSync expected)
    {
        var config = new FrameworkConfigManager(null!, appType);

        Assert.That(config.Get<FrameSync>(FrameworkSetting.FrameLimiter).Value, Is.EqualTo(expected));
    }

    [TestCase(AppType.Game)]
    [TestCase(AppType.Application)]
    public void TestPhoneStartsInVSync(AppType appType)
    {
        var config = new FrameworkConfigManager(null!, new FramePacing(appType, Mobile: true));

        Assert.That(config.Get<FrameSync>(FrameworkSetting.FrameLimiter).Value, Is.EqualTo(FrameSync.VSync));
    }

    [Test]
    public void TestPhoneAsksForTheOutputsOwnBuffer()
    {
        var phone = new FrameworkConfigManager(null!, new FramePacing(AppType.Game, Mobile: true));
        var desktop = new FrameworkConfigManager(null!, new FramePacing(AppType.Game, Mobile: false));

        Assert.Multiple(() =>
        {
            Assert.That(phone.Get<int>(FrameworkSetting.AudioDeviceBufferFrames).Value, Is.Zero);
            Assert.That(desktop.Get<int>(FrameworkSetting.AudioDeviceBufferFrames).Value, Is.EqualTo(SDLAudioManager.DEFAULT_DEVICE_BUFFER_FRAMES));
        });
    }
}
