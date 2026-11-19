// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// What takes over when what carries the low latency mode stops working.
/// </summary>
public class LowLatencyFallbackTest
{
    [TestCase(LowLatencyProvider.NvidiaReflex, LowLatencyProvider.GpuWait)]
    [TestCase(LowLatencyProvider.AmdAntiLag, LowLatencyProvider.GpuWait)]
    [TestCase(LowLatencyProvider.GpuWait, LowLatencyProvider.None)]
    [TestCase(LowLatencyProvider.None, LowLatencyProvider.None)]
    public void TheDriversOwnGivesWayToTheGpuWaitWhichGivesWayToNothing(LowLatencyProvider failed, LowLatencyProvider next) => Assert.That(LowLatencyFallback.After(failed), Is.EqualTo(next));
}
