// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// Turns a <c>CAMetalLayer</c>'s display sync off and on through <see cref="MacOSNative.SetDisplaySyncEnabled"/>, on a layer
/// made here with no window, and reads it back from the layer.
/// </summary>
[TestFixture]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class MacOSDisplaySyncTest
{
    private const string libobjc = "/usr/lib/libobjc.dylib";
    private const string quartz_core = "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";

    [DllImport(libobjc)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(libobjc)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_id(IntPtr receiver, IntPtr selector);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    private IntPtr layer;

    [SetUp]
    public void SetUp()
    {
        if (!RuntimeInfo.IsMacOS)
            Assert.Ignore("macOS only.");

        NativeLibrary.Load(quartz_core);

        IntPtr layerClass = objc_getClass("CAMetalLayer");
        Assert.That(layerClass, Is.Not.EqualTo(IntPtr.Zero), "QuartzCore has CAMetalLayer");

        layer = objc_msgSend_id(objc_msgSend_id(layerClass, sel_registerName("alloc")), sel_registerName("init"));
    }

    [TearDown]
    public void TearDown()
    {
        if (layer != IntPtr.Zero)
            objc_msgSend_void(layer, sel_registerName("release"));

        layer = IntPtr.Zero;
    }

    private bool displaySyncEnabled => objc_msgSend_bool(layer, sel_registerName("displaySyncEnabled"));

    [Test]
    public void ItTurnsTheLayersDisplaySyncOffAndOnAgain()
    {
        Assert.That(displaySyncEnabled, Is.True, "a new layer waits for the display");

        Assert.That(MacOSNative.SetDisplaySyncEnabled(layer, false), Is.True, "set off");
        Assert.That(displaySyncEnabled, Is.False, "off on the layer");

        Assert.That(MacOSNative.SetDisplaySyncEnabled(layer, true), Is.True, "set on");
        Assert.That(displaySyncEnabled, Is.True, "on on the layer");
    }

    [Test]
    public void NoLayerIsLeftAlone() => Assert.That(MacOSNative.SetDisplaySyncEnabled(IntPtr.Zero, false), Is.False);
}
