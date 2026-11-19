// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering.Direct3D11;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// The structures handed to NVIDIA's and AMD's drivers for Reflex and Anti-Lag 2 on Direct3D 11, against the sizes and
/// values in the vendors' headers. A driver refuses a structure whose size or version is off, so these are what can be
/// checked without the hardware.
/// </summary>
public unsafe class DriverLatencyInteropTest
{
    [Test]
    public void ReflexStructuresAreTheSizeNvapiExpects()
    {
        Assert.Multiple(() =>
        {
            Assert.That(sizeof(NvapiReflex.SleepModeParams), Is.EqualTo(44), "NV_SET_SLEEP_MODE_PARAMS_V1");
            Assert.That(sizeof(NvapiReflex.LatencyMarkerParams), Is.EqualTo(88), "NV_LATENCY_MARKER_PARAMS_V1");
        });
    }

    [Test]
    public void ReflexStructureVersionsCarryTheirSize()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvapiReflex.VersionOf<NvapiReflex.SleepModeParams>(1), Is.EqualTo(0x1002Cu));
            Assert.That(NvapiReflex.VersionOf<NvapiReflex.LatencyMarkerParams>(1), Is.EqualTo(0x10058u));
        });
    }

    [TestCase(LowLatencyMode.Off, 0, 0)]
    [TestCase(LowLatencyMode.On, 1, 0)]
    [TestCase(LowLatencyMode.Boost, 1, 1)]
    public void ReflexIsAskedForTheModeWithoutALimitOrMarkerOptimizing(LowLatencyMode mode, int lowLatency, int boost)
    {
        var parameters = NvapiReflex.CreateSleepModeParams(mode);

        Assert.Multiple(() =>
        {
            Assert.That(parameters.Version, Is.EqualTo(NvapiReflex.VersionOf<NvapiReflex.SleepModeParams>(1)));
            Assert.That(parameters.LowLatencyMode, Is.EqualTo(lowLatency));
            Assert.That(parameters.LowLatencyBoost, Is.EqualTo(boost));
            Assert.That(parameters.MinimumIntervalUs, Is.Zero);
            Assert.That(parameters.UseMarkersToOptimize, Is.Zero);
        });
    }

    [Test]
    public void AnNvapiStatusSaysWhatItMeans() => Assert.That(NvapiReflex.DescribeStatus(-6), Does.Contain("no NVIDIA device"));

    [Test]
    public void AntiLagDataIsTheSizeAmdsHeaderAsserts() => Assert.That(sizeof(AmdAntiLag2.ApiData), Is.EqualTo(32));

    [TestCase(true, 1u)]
    [TestCase(false, 2u)]
    public void AntiLagDataCarriesTheMode(bool enabled, uint mode)
    {
        var data = AmdAntiLag2.CreateApiData(enabled, null, 0);

        Assert.Multiple(() =>
        {
            Assert.That(data.Size, Is.EqualTo(32u));
            Assert.That(data.Version, Is.EqualTo(1u));
            Assert.That(data.Mode, Is.EqualTo(mode));
            Assert.That(data.MaxFps, Is.Zero);
        });
    }

    [Test]
    public void NeitherIsFoundWhereThereIsNoWindowsDriver()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Windows may have either driver.");

        var reflex = NvapiReflex.TryCreate(0, LowLatencyMode.On, out string reflexReason);
        var antiLag = AmdAntiLag2.TryCreate(out string antiLagReason);

        Assert.Multiple(() =>
        {
            Assert.That(reflex, Is.Null);
            Assert.That(reflexReason, Is.Not.Empty);
            Assert.That(antiLag, Is.Null);
            Assert.That(antiLagReason, Is.Not.Empty);
        });
    }
}
