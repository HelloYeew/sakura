// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Threading;

/// <summary>
/// An Objective-C autorelease pool around each frame of a framework thread on Apple platforms. An object an Objective-C
/// method hands back without keeping (most getters, and factories such as Core Haptics' players) waits in the thread's
/// innermost pool, and a .NET thread's own pool, where it has one, drains only when the thread ends: on a frame loop
/// that runs for the life of the app, every such object would live on. Pools nest, so the renderer's own pool for a
/// frame sits inside this one.
/// </summary>
internal static class AutoreleasePool
{
    private const string libobjc = "/usr/lib/libobjc.dylib";

    private static readonly bool enabled = RuntimeInfo.IsApple;

    [DllImport(libobjc)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(libobjc)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    /// <summary>
    /// Opens a pool on the calling thread; <see cref="IntPtr.Zero"/> off Apple platforms, which have none.
    /// </summary>
    public static IntPtr Push() => enabled ? objc_autoreleasePoolPush() : IntPtr.Zero;

    /// <summary>
    /// Drains the pool <see cref="Push"/> opened on this thread, with any opened inside it since.
    /// </summary>
    public static void Pop(IntPtr pool)
    {
        if (pool != IntPtr.Zero)
            objc_autoreleasePoolPop(pool);
    }
}
