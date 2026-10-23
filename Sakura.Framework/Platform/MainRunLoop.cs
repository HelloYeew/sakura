// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Platform;

/// <summary>
/// The main thread's CoreFoundation run loop, which UIKit delivers touches, lifecycle changes and everything else
/// through. SDL can only wait for events on iOS by polling a thousand times a second, so the event thread sleeps here
/// instead: anything UIKit has for the app ends the sleep, as does <see cref="WakeUp"/>.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class MainRunLoop
{
    private const string core_foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(core_foundation)]
    private static extern IntPtr CFRunLoopGetMain();

    [DllImport(core_foundation)]
    private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled);

    [DllImport(core_foundation)]
    private static extern void CFRunLoopWakeUp(IntPtr runLoop);

    private static readonly IntPtr main = CFRunLoopGetMain();

    // A CFStringRef the framework exports, rather than one made here: modes are compared by name, but this is the one
    // UIKit uses.
    private static readonly IntPtr default_mode = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(core_foundation), "kCFRunLoopDefaultMode"));

    /// <summary>
    /// Runs the run loop until it has handled something, <see cref="WakeUp"/> is called or <paramref name="seconds"/>
    /// have passed. Called on the main thread.
    /// </summary>
    public static void Wait(double seconds) => CFRunLoopRunInMode(default_mode, seconds, true);

    /// <summary>
    /// Ends a <see cref="Wait"/> early. Safe to call from any thread.
    /// </summary>
    public static void WakeUp() => CFRunLoopWakeUp(main);
}
