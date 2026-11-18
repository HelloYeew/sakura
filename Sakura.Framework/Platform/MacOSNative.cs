// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Platform;

/// <summary>
/// Minimal Objective-C runtime interop for the few macOS-only calls the framework needs but SDL doesn't
/// expose. Each is guarded by <see cref="RuntimeInfo.IsMacOS"/>. AppKit calls must be made on the main
/// thread; each method says which thread it wants.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class MacOSNative
{
    private const string libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(libobjc, EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(libobjc, EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_id(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool_id(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

    // A double comes back in a floating-point register through the plain objc_msgSend on arm64 and x86-64 alike: only a long
    // double needs objc_msgSend_fpret.
    [DllImport(libobjc, EntryPoint = "objc_msgSend")]
    private static extern double objc_msgSend_double(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// How much two refresh intervals may differ, in seconds, and still be one fixed rate.
    /// </summary>
    private const double fixed_refresh_tolerance = 1e-6;

    /// <summary>
    /// The <c>NSScreen</c> an <c>NSWindow</c> is mostly on. Call it on the main thread.
    /// </summary>
    /// <returns>The screen, or zero off macOS, for no window, or for a window on no screen.</returns>
    public static IntPtr ScreenOf(IntPtr nsWindow) => RuntimeInfo.IsMacOS && nsWindow != IntPtr.Zero ? objc_msgSend(nsWindow, sel_registerName("screen")) : IntPtr.Zero;

    /// <summary>
    /// Reads a screen's <c>minimumRefreshInterval</c> and <c>maximumRefreshInterval</c>: the shortest and longest a frame
    /// may stay on it, in seconds. They differ for a screen whose refresh varies (ProMotion, Adaptive-Sync). Call it on the
    /// main thread.
    /// </summary>
    /// <param name="screen">An <c>NSScreen</c>.</param>
    /// <param name="shortest">The shortest interval, the inverse of the top rate.</param>
    /// <param name="longest">The longest interval, the inverse of the lowest rate.</param>
    /// <returns>Whether both were read. False off macOS, for no screen, and before macOS 12, whose screens have neither.</returns>
    public static bool TryGetRefreshIntervals(IntPtr screen, out double shortest, out double longest)
    {
        shortest = 0;
        longest = 0;

        if (!RuntimeInfo.IsMacOS || screen == IntPtr.Zero)
            return false;

        IntPtr minimum = sel_registerName("minimumRefreshInterval");

        // An unknown selector would raise an Objective-C exception, which ends the process.
        if (!objc_msgSend_bool_id(screen, sel_registerName("respondsToSelector:"), minimum))
            return false;

        shortest = objc_msgSend_double(screen, minimum);
        longest = objc_msgSend_double(screen, sel_registerName("maximumRefreshInterval"));
        return shortest > 0 && longest > 0;
    }

    /// <summary>
    /// Whether a screen's refresh varies to meet each frame: its shortest and longest refresh intervals differ. It's
    /// Apple's own test for whether a fullscreen game can pace itself for a variable refresh rate. Call it on the main thread.
    /// </summary>
    /// <param name="screen">An <c>NSScreen</c>.</param>
    public static bool ScreenRefreshVaries(IntPtr screen) => TryGetRefreshIntervals(screen, out double shortest, out double longest) && !Precision.AlmostEquals(shortest, longest, fixed_refresh_tolerance);

    /// <summary>
    /// Sets <c>displaySyncEnabled</c> on a <c>CAMetalLayer</c>: whether its presents wait for the display's refresh. With it
    /// off a frame is shown as soon as it's presented, which can tear in fullscreen. Call it on the thread that draws into the
    /// layer, between frames, as the layer's other drawing settings are.
    /// </summary>
    /// <returns>Whether it was set. False off macOS, where the layer has no such property.</returns>
    public static bool SetDisplaySyncEnabled(IntPtr metalLayer, bool enabled)
    {
        if (!RuntimeInfo.IsMacOS || metalLayer == IntPtr.Zero)
            return false;

        IntPtr setter = sel_registerName("setDisplaySyncEnabled:");

        // An unknown selector would raise an Objective-C exception, which ends the process.
        if (!objc_msgSend_bool_id(metalLayer, sel_registerName("respondsToSelector:"), setter))
            return false;

        objc_msgSend_void_bool(metalLayer, setter, enabled);
        return true;
    }

    /// <summary>
    /// Opens the macOS system Character Viewer (emoji &amp; symbols picker) via
    /// <c>[[NSApplication sharedApplication] orderFrontCharacterPalette:nil]</c>. No-op off macOS or if
    /// the runtime symbols can't be resolved. Must be called on the main thread.
    /// </summary>
    public static void ShowCharacterPalette()
    {
        if (!RuntimeInfo.IsMacOS)
            return;

        try
        {
            IntPtr nsApplication = objc_getClass("NSApplication");
            if (nsApplication == IntPtr.Zero)
                return;

            IntPtr sharedApp = objc_msgSend(nsApplication, sel_registerName("sharedApplication"));
            if (sharedApp == IntPtr.Zero)
                return;

            objc_msgSend_void_id(sharedApp, sel_registerName("orderFrontCharacterPalette:"), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            // Interop failures must never take down the input loop.
            Logger.Error($"Failed to open macOS character palette: {ex.Message}");
        }
    }
}
