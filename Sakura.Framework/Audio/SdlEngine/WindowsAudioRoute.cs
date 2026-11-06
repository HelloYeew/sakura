// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The Windows shared audio engine on the endpoint SDL plays on. Past SDL's writes into the endpoint's buffer, the engine
/// takes a period from every app, mixes it and hands it on to the device, which holds the sound for that period more.
/// The driver, the hardware and the way past the device add their own, which Windows doesn't report, so this stops
/// at the device. It gives no name, so the output keeps the one SDL lists it under.
/// </summary>
internal sealed class WindowsAudioRoute : ISystemAudioRoute
{
    // The endpoint SDL plays on, by its name, or empty for the system's default output.
    private string playingDevice = string.Empty;

    private EnginePeriod lastLogged;
    private bool loggedMissing;

    public string Name => string.Empty;

    public double LatencyMilliseconds => tryRead(out var period) ? period.Milliseconds : 0;

    public bool ReachesListener => false;

    public void SetPlayingDevice(string name)
    {
        if (name == playingDevice)
            return;

        playingDevice = name;
        loggedMissing = false;
    }

    /// <summary>
    /// Reads the engine's period on the endpoint the sound plays on now, and says so when it's changed. Looked up again
    /// at each read, once a second and never on the audio callback, so an endpoint plugged in again or a default that
    /// moved is followed without keeping anything of COM's between reads.
    /// </summary>
    /// <returns>Whether the endpoint was found and said.</returns>
    private bool tryRead(out EnginePeriod period)
    {
        period = default;

        nint enumerator = WasapiNative.CreateEnumerator();

        if (enumerator == 0)
            return missing("Windows' audio endpoints can't be read");

        nint device = 0;

        try
        {
            device = playingDevice.Length == 0 ? WasapiNative.GetDefaultOutput(enumerator) : WasapiNative.FindOutput(enumerator, playingDevice);

            if (device == 0)
            {
                return missing(playingDevice.Length == 0
                    ? "Windows has no default output"
                    : $"Windows has no output named {playingDevice}");
            }

            if (!WasapiNative.TryGetEnginePeriod(device, out uint frames, out uint sampleRate, out bool current))
                return missing($"The audio engine's period on {WasapiNative.GetName(device) ?? playingDevice} can't be read");

            period = new EnginePeriod(frames, sampleRate, current);
            loggedMissing = false;

            if (period != lastLogged)
            {
                lastLogged = period;
                Logger.Verbose($"Audio engine on {WasapiNative.GetName(device) ?? "the default output"}: a period of {frames} frames at {sampleRate} Hz "
                               + $"({period.Milliseconds:F1} ms, {(current ? "its current one" : "its default")}), after SDL's buffers. "
                               + "What the driver and the device hold isn't reported.");
            }

            return true;
        }
        finally
        {
            WasapiNative.Release(device);
            WasapiNative.Release(enumerator);
        }
    }

    /// <summary>
    /// Says once why nothing is counted after SDL's buffers.
    /// </summary>
    /// <returns>False, for the read that failed.</returns>
    private bool missing(string why)
    {
        if (!loggedMissing)
        {
            loggedMissing = true;
            Logger.Verbose($"{why}, so the delay after SDL's buffers isn't counted.");
        }

        return false;
    }

    /// <summary>
    /// The shared engine's period on an endpoint.
    /// </summary>
    /// <param name="Frames">How long it is, in the engine's frames.</param>
    /// <param name="SampleRate">The rate the engine mixes at.</param>
    /// <param name="Current">Whether it's the period now, rather than the endpoint's default.</param>
    private readonly record struct EnginePeriod(uint Frames, uint SampleRate, bool Current)
    {
        public double Milliseconds => Frames * 1000.0 / SampleRate;
    }
}
