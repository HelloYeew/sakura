// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Logging;
using static Sakura.Framework.Audio.SdlEngine.CoreAudioNative;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The Core Audio device the sound plays on, on macOS. Past SDL's own buffers, or straight from the HAL output's render,
/// the sound waits in the device's I/O buffer and its safety offset, then goes through the device and its stream: a few
/// milliseconds on a wired device, and whatever a Bluetooth one reports. It gives no name, so the output keeps the one
/// SDL lists it under, which players already know.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class MacOSAudioRoute : ISystemAudioRoute
{
    // The device the sound plays on, by SDL's name for it, or empty for the system's default output.
    private string playingDevice = string.Empty;

    // The device that name was found as. 0 to look it up again: plugged back in, a device comes back as a new one.
    private uint namedDevice;

    private DeviceLatency lastLogged;
    private bool loggedMissing;

    public string Name => string.Empty;

    public double LatencyMilliseconds => tryRead(out var latency) ? latency.TotalMilliseconds : 0;

    /// <summary>
    /// The I/O buffer and the safety offset: what an output's own timestamps cover, as they're taken at the device's
    /// side of both.
    /// </summary>
    public double BufferMilliseconds => tryRead(out var latency) ? latency.BufferMilliseconds : 0;

    public void SetPlayingDevice(string name)
    {
        if (name == playingDevice)
            return;

        playingDevice = name;
        namedDevice = 0;
        loggedMissing = false;
    }

    /// <summary>
    /// Reads the delays of the device the sound plays on now, and says so when they've changed.
    /// </summary>
    /// <returns>Whether the device was found and said.</returns>
    private bool tryRead(out DeviceLatency latency)
    {
        uint device = playingDevice.Length == 0 ? DefaultOutputDevice() : namedDevice != 0 ? namedDevice : FindOutputDevice(playingDevice);

        if (device == 0 || !DeviceLatency.TryRead(device, out latency))
        {
            namedDevice = 0;
            latency = default;

            if (!loggedMissing)
            {
                loggedMissing = true;
                Logger.Verbose(playingDevice.Length == 0
                    ? "Core Audio has no default output device to read, so the device's own delay isn't counted."
                    : $"Core Audio has no output device named {playingDevice}, so the device's own delay isn't counted.");
            }

            return false;
        }

        if (playingDevice.Length > 0)
            namedDevice = device;

        loggedMissing = false;

        if (latency != lastLogged)
        {
            lastLogged = latency;
            Logger.Verbose($"Output device: {latency.Describe()}");
        }

        return true;
    }

    /// <summary>
    /// What an output device holds the sound for, in its own frames, as it reports it.
    /// </summary>
    /// <param name="Device">The device.</param>
    /// <param name="SampleRate">The rate the device runs at.</param>
    /// <param name="BufferFrames">The I/O buffer, which is this process's own.</param>
    /// <param name="SafetyOffset">How far ahead of the device's position the I/O buffer is written.</param>
    /// <param name="DeviceFrames">The device's latency.</param>
    /// <param name="StreamFrames">Its first output stream's latency.</param>
    private readonly record struct DeviceLatency(uint Device, double SampleRate, uint BufferFrames, uint SafetyOffset, uint DeviceFrames, uint StreamFrames)
    {
        public double BufferMilliseconds => (BufferFrames + SafetyOffset) / SampleRate * 1000.0;

        public double TotalMilliseconds => (BufferFrames + SafetyOffset + DeviceFrames + StreamFrames) / SampleRate * 1000.0;

        /// <summary>
        /// Reads <paramref name="device"/>'s delays.
        /// </summary>
        /// <returns>Whether it's there and said what it runs at.</returns>
        public static bool TryRead(uint device, out DeviceLatency latency)
        {
            latency = default;

            if (!TryGet(device, PROPERTY_NOMINAL_SAMPLE_RATE, SCOPE_GLOBAL, out double sampleRate) || sampleRate <= 0
                || !TryGet(device, PROPERTY_BUFFER_FRAME_SIZE, SCOPE_GLOBAL, out uint bufferFrames))
                return false;

            // Either can be missing on a device that has no delay to report.
            TryGet(device, PROPERTY_SAFETY_OFFSET, SCOPE_OUTPUT, out uint safetyOffset);
            TryGet(device, PROPERTY_LATENCY, SCOPE_OUTPUT, out uint deviceFrames);

            uint streamFrames = 0;
            uint[] streams = GetObjects(device, PROPERTY_STREAMS, SCOPE_OUTPUT);

            if (streams.Length > 0)
                TryGet(streams[0], PROPERTY_LATENCY, SCOPE_GLOBAL, out streamFrames);

            latency = new DeviceLatency(device, sampleRate, bufferFrames, safetyOffset, deviceFrames, streamFrames);
            return true;
        }

        /// <summary>
        /// The device and what it adds, for the log.
        /// </summary>
        public string Describe()
        {
            string name = GetDeviceName(Device) ?? $"device {Device}";
            // A four-character code: 'bltn', 'usb ', 'blue' (Bluetooth), 'blea' (Bluetooth LE), 'hdmi', 'airp' (AirPlay).
            string transport = TryGet(Device, PROPERTY_TRANSPORT_TYPE, SCOPE_GLOBAL, out uint type)
                ? new string(new[] { (char)(type >> 24), (char)((type >> 16) & 0xff), (char)((type >> 8) & 0xff), (char)(type & 0xff) })
                : "unknown";

            return $"{name} (over '{transport}', {SampleRate:0} Hz), {TotalMilliseconds:F1} ms from its I/O buffer on: "
                   + $"an I/O buffer of {BufferFrames} frames, a safety offset of {SafetyOffset}, a device latency of {DeviceFrames} "
                   + $"and a stream latency of {StreamFrames}";
        }
    }
}
