// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// When OpenGL ES frames reached the screen on Android, from <c>EGL_ANDROID_get_frame_timestamps</c>: the EGL
/// counterpart of <c>VK_GOOGLE_display_timing</c>, read from the same timestamps Android's window keeps of each
/// frame. For <see cref="GLRenderer"/>, on the draw thread, with its context current.
/// </summary>
/// <remarks>
/// Only where the display reports when frames are shown (present fences): most phones, not the emulator.
/// </remarks>
internal sealed unsafe class EglFrameTimestamps
{
    private const string egl = "EGL";

    private const int egl_extensions = 0x3055;
    private const int egl_draw = 0x3059;
    private const int egl_timestamps_android = 0x3430;
    private const int egl_display_present_time_android = 0x343A;
    private const long egl_timestamp_pending_android = -2;

    private const int max_pending = 16;

    [DllImport(egl)]
    private static extern nint eglGetCurrentDisplay();

    [DllImport(egl)]
    private static extern nint eglGetCurrentSurface(int readDraw);

    [DllImport(egl)]
    private static extern nint eglQueryString(nint display, int name);

    [DllImport(egl)]
    private static extern uint eglSurfaceAttrib(nint display, nint surface, int attribute, int value);

    [DllImport(egl)]
    private static extern nint eglGetProcAddress(string name);

    private readonly nint display;
    private readonly nint surface;
    private readonly delegate* unmanaged<nint, nint, ulong*, uint> getNextFrameId;
    private readonly delegate* unmanaged<nint, nint, ulong, int, int*, long*, uint> getFrameTimestamps;

    // The latest frames' IDs, each with when the update it shows was made, until when it was shown is known.
    private readonly (ulong Id, long ContentTicks)[] pending = new (ulong, long)[max_pending];
    private int next;

    private EglFrameTimestamps(nint display, nint surface, delegate* unmanaged<nint, nint, ulong*, uint> getNextFrameId,
                               delegate* unmanaged<nint, nint, ulong, int, int*, long*, uint> getFrameTimestamps)
    {
        this.display = display;
        this.surface = surface;
        this.getNextFrameId = getNextFrameId;
        this.getFrameTimestamps = getFrameTimestamps;
    }

    /// <summary>
    /// The surface drawn to on this thread, or 0 off Android or without one: what timing is kept for, and a new one
    /// (the app back from the background) needs <see cref="TryCreate"/> again.
    /// </summary>
    public static nint CurrentSurface()
    {
        if (!OperatingSystem.IsAndroid())
            return 0;

        try
        {
            return eglGetCurrentSurface(egl_draw);
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Timing for the context current on this thread, or null where Android's EGL can't say when its frames are shown.
    /// </summary>
    public static EglFrameTimestamps? TryCreate()
    {
        if (!OperatingSystem.IsAndroid())
            return null;

        try
        {
            nint display = eglGetCurrentDisplay();
            nint surface = eglGetCurrentSurface(egl_draw);

            if (display == 0 || surface == 0)
                return null;

            string extensions = Marshal.PtrToStringAnsi(eglQueryString(display, egl_extensions)) ?? string.Empty;

            if (!extensions.Contains("EGL_ANDROID_get_frame_timestamps", StringComparison.Ordinal))
                return null;

            var supported = (delegate* unmanaged<nint, nint, int, uint>)eglGetProcAddress("eglGetFrameTimestampSupportedANDROID");
            var nextId = (delegate* unmanaged<nint, nint, ulong*, uint>)eglGetProcAddress("eglGetNextFrameIdANDROID");
            var timestamps = (delegate* unmanaged<nint, nint, ulong, int, int*, long*, uint>)eglGetProcAddress("eglGetFrameTimestampsANDROID");

            if (supported == null || nextId == null || timestamps == null)
                return null;

            // The window only keeps them once asked to, and when frames are shown only where the display says.
            if (eglSurfaceAttrib(display, surface, egl_timestamps_android, 1) == 0 || supported(display, surface, egl_display_present_time_android) == 0)
                return null;

            return new EglFrameTimestamps(display, surface, nextId, timestamps);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Notes the frame about to be swapped as showing the update made at <paramref name="contentTicks"/>.
    /// </summary>
    public void FrameSwapping(long contentTicks)
    {
        ulong id;

        if (getNextFrameId(display, surface, &id) == 0)
            return;

        pending[next] = (id, contentTicks);
        next = (next + 1) % max_pending;
    }

    /// <summary>
    /// Measures the frames noted that are known to have reached the screen by now.
    /// </summary>
    public void Measure(DisplayLatency latency)
    {
        int name = egl_display_present_time_android;

        for (int i = 0; i < max_pending; i++)
        {
            var (id, contentTicks) = pending[i];

            if (contentTicks == 0)
                continue;

            long shown;

            // False for a frame older than the window keeps: it never will be known.
            if (getFrameTimestamps(display, surface, id, 1, &name, &shown) == 0)
            {
                pending[i] = default;
                continue;
            }

            if (shown == egl_timestamp_pending_android)
                continue;

            pending[i] = default;

            // Invalid (-1) for a frame that was never shown.
            if (shown > 0)
                latency.Add(DisplayLatency.Between(contentTicks, DisplayLatency.TicksFromNanoseconds(shown)));
        }
    }
}
