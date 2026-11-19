// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Configurations;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// Where the low latency mode comes from: the setting, its default from the host's options, and the environment variable
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

    [TestCase(AppType.Game)]
    [TestCase(AppType.Application)]
    public void AnInstallStartsWithItOff(AppType appType)
    {
        var config = new FrameworkConfigManager(null!, appType);

        Assert.That(config.Get<LowLatencyMode>(FrameworkSetting.LowLatencyMode).Value, Is.EqualTo(LowLatencyMode.Off));
    }

    [Test]
    public void TheHostCanStartAnInstallWithItOn()
    {
        var config = new FrameworkConfigManager(null!, new FramePacing(AppType.Game, false), LowLatencyMode.On);

        Assert.That(config.Get<LowLatencyMode>(FrameworkSetting.LowLatencyMode).Value, Is.EqualTo(LowLatencyMode.On));
    }

    [Test]
    public void HostOptionsDefaultToOff() => Assert.That(new HostOptions().DefaultLowLatencyMode, Is.EqualTo(LowLatencyMode.Off));
}
