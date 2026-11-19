// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Configurations;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// Where the low latency mode comes from: the setting, which a desktop game starts with on, and the environment variable
/// that overrides it for comparing.
/// </summary>
public class LowLatencyModeTest
{
    [TestCase("off", LowLatencyMode.Off)]
    [TestCase("on", LowLatencyMode.On)]
    [TestCase("boost", LowLatencyMode.Boost)]
    [TestCase(" Boost ", LowLatencyMode.Boost)]
    [TestCase("ON", LowLatencyMode.On)]
    public void TheVariableNamesAMode(string value, LowLatencyMode expected) => Assert.That(AppHost.ParseLowLatencyMode(value), Is.EqualTo(expected));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("1")]
    [TestCase("reflex")]
    public void AnythingElseLeavesTheSettingInCharge(string? value) => Assert.That(AppHost.ParseLowLatencyMode(value!), Is.Null);

    [TestCase(AppType.Game, false, LowLatencyMode.On)]
    [TestCase(AppType.Application, false, LowLatencyMode.Off)]
    [TestCase(AppType.Game, true, LowLatencyMode.Off)]
    [TestCase(AppType.Application, true, LowLatencyMode.Off)]
    public void OnlyADesktopGameStartsWithItOn(AppType appType, bool mobile, LowLatencyMode expected)
    {
        var config = new FrameworkConfigManager(null!, new FramePacing(appType, mobile));

        Assert.That(config.Get<LowLatencyMode>(FrameworkSetting.LowLatencyMode).Value, Is.EqualTo(expected));
    }
}
