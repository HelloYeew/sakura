// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

/// <summary>
/// The calling thread's registration with the Windows Multimedia Class Scheduler Service (MMCSS) as a
/// "Games" task. Must be disposed on the thread that registered.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMultimediaScheduler : IDisposable
{
    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

    private const string task_name = "Games";

    private readonly IntPtr handle;

    private WindowsMultimediaScheduler(IntPtr handle)
    {
        this.handle = handle;
    }

    /// <summary>
    /// Registers the calling thread.
    /// </summary>
    /// <param name="threadName">The thread's name, for logging.</param>
    /// <returns>The registration, or null if MMCSS is unavailable (for example, the service is disabled).</returns>
    public static WindowsMultimediaScheduler? Register(string threadName)
    {
        uint taskIndex = 0;
        IntPtr handle;

        try
        {
            handle = AvSetMmThreadCharacteristicsW(task_name, ref taskIndex);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Logger.Debug($"'{threadName}' not registered with MMCSS: {e.Message}");
            return null;
        }

        if (handle == IntPtr.Zero)
        {
            Logger.Debug($"'{threadName}' not registered with MMCSS (error {Marshal.GetLastPInvokeError()})");
            return null;
        }

        Logger.Debug($"'{threadName}' registered with MMCSS as a '{task_name}' task");
        return new WindowsMultimediaScheduler(handle);
    }

    public void Dispose() => AvRevertMmThreadCharacteristics(handle);
}
