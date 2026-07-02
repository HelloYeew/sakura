// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

/// <summary>
/// Minimal Objective-C runtime interop for the few macOS-only AppKit calls the framework needs but
/// SDL doesn't expose. Everything here must be called on the main thread and guarded by
/// <see cref="RuntimeInfo.IsMacOS"/>
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
