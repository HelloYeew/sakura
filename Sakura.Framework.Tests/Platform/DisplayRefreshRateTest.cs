// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// The refresh rate a window times its frames by, read from SDL's display mode without rounding it to a whole number.
/// </summary>
[TestFixture]
public class DisplayRefreshRateTest
{
    [Test]
    public void TheExactFractionIsKept() => Assert.That(SDLWindow.RefreshRateOf(60000, 1001, 59.94f), Is.EqualTo(60000.0 / 1001).Within(1e-12));

    [Test]
    public void AWholeRateStaysWhole() => Assert.That(SDLWindow.RefreshRateOf(120, 1, 120f), Is.EqualTo(120));

    [Test]
    public void WithoutAFractionTheFloatIsUsedUnrounded() => Assert.That(SDLWindow.RefreshRateOf(0, 0, 143.86f), Is.EqualTo(143.86).Within(1e-4));

    [Test]
    public void AModeWithNoRateHasNone() => Assert.That(SDLWindow.RefreshRateOf(0, 0, 0), Is.Zero);

    [Test]
    public void AWindowWithNoExactRateReportsItsWholeOne() => Assert.That(((IWindow)new HeadlessWindow()).DisplayRefreshRate, Is.EqualTo(60));
}
