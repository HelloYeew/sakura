// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
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
}
