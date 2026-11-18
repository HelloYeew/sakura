// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// Reads the main screen's shortest and longest refresh intervals through <see cref="MacOSNative.TryGetRefreshIntervals"/>,
/// the figures macOS says whether a screen's refresh varies by, and the step between them through
/// <see cref="MacOSNative.TryGetRefreshStep"/>, with no window.
/// </summary>
[TestFixture]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class MacOSScreenRefreshTest
{
    private const string libobjc = "/usr/lib/libobjc.dylib";
    private const string app_kit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    [DllImport(libobjc)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(libobjc)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_id(IntPtr receiver, IntPtr selector);

    private IntPtr screen;

    [SetUp]
    public void SetUp()
    {
        if (!RuntimeInfo.IsMacOS)
            Assert.Ignore("macOS only.");

        NativeLibrary.Load(app_kit);

        IntPtr screenClass = objc_getClass("NSScreen");
        Assert.That(screenClass, Is.Not.EqualTo(IntPtr.Zero), "AppKit has NSScreen");

        screen = objc_msgSend_id(screenClass, sel_registerName("mainScreen"));

        if (screen == IntPtr.Zero)
            Assert.Ignore("No screen to read: the process has no window server.");
    }

    [Test]
    public void TheMainScreensIntervalsAreAPlausibleRange()
    {
        Assert.That(MacOSNative.TryGetRefreshIntervals(screen, out double shortest, out double longest), Is.True, "read");

        Assert.Multiple(() =>
        {
            Assert.That(1 / shortest, Is.InRange(24, 1000), "the top rate, in Hz");
            Assert.That(longest, Is.GreaterThanOrEqualTo(shortest), "the longest interval is the lowest rate");
        });
    }

    [Test]
    public void ItVariesWhereTheIntervalsDiffer()
    {
        MacOSNative.TryGetRefreshIntervals(screen, out double shortest, out double longest);

        Assert.That(MacOSNative.ScreenRefreshVaries(screen), Is.EqualTo(longest - shortest > 1e-6));
    }

    [Test]
    public void TheMainScreensStepFitsInsideItsIntervals()
    {
        MacOSNative.TryGetRefreshIntervals(screen, out _, out double longest);

        Assert.That(MacOSNative.TryGetRefreshStep(screen, out double step), Is.True, "read");
        Assert.That(step, Is.InRange(0, longest), "0 for a screen that refreshes at any moment, else a step no longer than its longest interval");
    }

    [Test]
    public void NoScreenIsLeftAlone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MacOSNative.TryGetRefreshIntervals(IntPtr.Zero, out _, out _), Is.False);
            Assert.That(MacOSNative.TryGetRefreshStep(IntPtr.Zero, out double step), Is.False);
            Assert.That(step, Is.Zero, "nothing read");
            Assert.That(MacOSNative.ScreenRefreshVaries(IntPtr.Zero), Is.False);
            Assert.That(MacOSNative.ScreenOf(IntPtr.Zero), Is.EqualTo(IntPtr.Zero));
        });
    }
}
