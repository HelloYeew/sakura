// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

/// <summary>
/// What a frame thread asks Windows for at each <see cref="ThreadActivity"/>. Windows already sets a thread's quality of
/// service by its window, high while focused and lower behind other windows, so a thread leaves it to the system,
/// except while nothing of the app can be seen: then it asks for EcoQoS, which runs it on efficient cores at low clocks.
/// </summary>
/// <remarks>
/// The thread's priority is .NET's, set when it starts: Windows is the one platform where those priorities help.
/// </remarks>
internal sealed class WindowsThreadPolicy : IThreadPolicy
{
    // THREAD_INFORMATION_CLASS's ThreadPowerThrottling, and THREAD_POWER_THROTTLING_CURRENT_VERSION.
    private const int thread_power_throttling = 3;
    private const uint power_throttling_version = 1;

    // THREAD_POWER_THROTTLING_EXECUTION_SPEED: the one thing a thread can ask to have throttled.
    private const uint execution_speed = 0x1;

    private readonly string threadName;
    private bool unsupported;

    /// <param name="threadName">The thread's name, for logging.</param>
    [SupportedOSPlatform("windows")]
    public WindowsThreadPolicy(string threadName)
    {
        this.threadName = threadName;
    }

    /// <summary>
    /// Whether a thread asks for EcoQoS at a level.
    /// </summary>
    internal static bool UsesEcoQoS(ThreadActivity activity) => activity == ThreadActivity.Hidden;

    public string Apply(ThreadActivity activity)
    {
        if (unsupported)
            return "QoS left to the system";

        bool eco = UsesEcoQoS(activity);

        // A control mask of 0 hands the choice back to the system; with the bit set, the state mask turns it on or off.
        var state = new PowerThrottlingState
        {
            Version = power_throttling_version,
            ControlMask = eco ? execution_speed : 0,
            StateMask = eco ? execution_speed : 0
        };

        if (!SetThreadInformation(GetCurrentThread(), thread_power_throttling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>()))
        {
            // Windows 10 before 1709 has no power throttling for threads; nothing to undo either.
            unsupported = true;
            Logger.Debug($"{threadName} can't ask for EcoQoS (error {Marshal.GetLastPInvokeError()}); its QoS stays with the system.");
            return "QoS left to the system";
        }

        return eco ? "EcoQoS" : "QoS left to the system";
    }

    public void BeginFrame(double frameMilliseconds)
    {
    }

    public void EndFrame()
    {
    }

    public void Dispose()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadInformation(IntPtr thread, int informationClass, ref PowerThrottlingState information, uint size);
}
