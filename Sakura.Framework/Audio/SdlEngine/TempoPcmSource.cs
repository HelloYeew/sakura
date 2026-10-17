// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Puts a <see cref="TimeStretcher"/> in front of another <see cref="IPcmSource"/>, for
/// <see cref="IAudioChannel.Tempo"/> on the managed mixer. The native engine does the same thing in
/// <see cref="NativeStreamFeeder"/>.
/// </summary>
/// <remarks>
/// <para>
/// At a tempo of 1 this is a straight pass-through and the stretcher is never touched, so a track
/// that never changes speed plays exactly the frames it always did.
/// </para>
/// <para>
/// A stretcher runs at one tempo for its whole life. Changing speed from normal just starts one
/// where the source is, which is seamless. Changing it while one is already running moves the source
/// back to where the listener is and starts a fresh one there, because the stretcher holds input it
/// has read but not played yet, and that audio belongs to the old speed's timeline. The cost is the
/// same short refill gap as a seek, and in exchange the position stays exact.
/// </para>
/// <para>
/// Threading follows <see cref="StreamingPcmSource"/>: <see cref="ReadFrames"/> runs on the mix
/// thread; <see cref="Seek"/> and <see cref="SetTempo"/> run on the audio thread; the position is read
/// from anywhere. One lock covers all of it.
/// </para>
/// </remarks>
internal sealed class TempoPcmSource : IPcmSource
{
    private readonly IPcmSource inner;
    private readonly TimeStretcher stretcher;
    private readonly int sampleRate;
    private readonly int channels;

    private readonly Lock sync = new Lock();

    private float[] inputScratch = Array.Empty<float>();

    /// <summary>
    /// The tempo the current stretch is running at, or 1 while passing straight through.
    /// </summary>
    private double sessionTempo = 1;

    /// <summary>
    /// Where in the source the current stretch started, and how many frames it has produced since.
    /// </summary>
    private double sessionStartMs;

    private long sessionOutputFrames;

    public TempoPcmSource(IPcmSource inner, int sampleRate, int channels)
    {
        this.inner = inner;
        this.sampleRate = sampleRate;
        this.channels = channels;

        stretcher = new TimeStretcher(sampleRate, channels);
    }

    /// <summary>
    /// The tempo the audio is currently coming out at.
    /// </summary>
    public double Tempo
    {
        get
        {
            lock (sync)
                return sessionTempo;
        }
    }

    public double LengthMs => inner.LengthMs;

    public double PositionMs
    {
        get
        {
            lock (sync)
                return positionUnlocked();
        }
    }

    public bool Ended
    {
        get
        {
            lock (sync)
                return sessionTempo == 1 ? inner.Ended : stretcher.Drained;
        }
    }

    /// <summary>
    /// Changes the playback speed.
    /// </summary>
    /// <returns>
    /// Whether the source had to be moved to do it, in which case the caller owes the same clean-up as
    /// after a seek: the resampler's window and the filter's state hold audio from the old timeline.
    /// </returns>
    public bool SetTempo(double tempo)
    {
        tempo = Math.Clamp(tempo, TimeStretcher.MIN_TEMPO, TimeStretcher.MAX_TEMPO);

        lock (sync)
        {
            if (tempo == sessionTempo)
                return false;

            if (sessionTempo == 1)
            {
                // Nothing read ahead to throw away: start stretching from where the source already is.
                startSession(inner.PositionMs, tempo);
                return false;
            }

            double position = positionUnlocked();

            inner.Seek(position);
            startSession(position, tempo);
            return true;
        }
    }

    public void Seek(double milliseconds)
    {
        lock (sync)
        {
            double target = Math.Max(0, milliseconds);

            inner.Seek(target);
            startSession(target, sessionTempo);
        }
    }

    public int ReadFrames(Span<float> destination, int frameCount)
    {
        lock (sync)
        {
            if (sessionTempo == 1)
                return inner.ReadFrames(destination, frameCount);

            frameCount = Math.Min(frameCount, destination.Length / channels);

            int produced = 0;

            while (produced < frameCount)
            {
                if (stretcher.AvailableFrames > 0)
                {
                    produced += stretcher.ReceiveOutput(destination.Slice(produced * channels), frameCount - produced);
                    continue;
                }

                if (stretcher.Drained || !pullInput())
                    break;
            }

            sessionOutputFrames += produced;
            return produced;
        }
    }

    /// <summary>
    /// Feeds the stretcher what its next sequence needs, or flushes it once the source has ended.
    /// </summary>
    /// <returns>False when nothing could be fed: a streaming source that has not decoded far enough.</returns>
    private bool pullInput()
    {
        int wanted = Math.Max(1, stretcher.InputFramesWanted);

        if (inputScratch.Length < wanted * channels)
            inputScratch = new float[wanted * channels];

        int read = inner.ReadFrames(inputScratch, wanted);

        if (read > 0)
        {
            stretcher.PutInput(inputScratch.AsSpan(0, read * channels));
            return true;
        }

        if (!inner.Ended)
            return false;

        stretcher.Flush();
        return stretcher.AvailableFrames > 0;
    }

    private void startSession(double startMs, double tempo)
    {
        sessionTempo = tempo;
        sessionStartMs = startMs;
        sessionOutputFrames = 0;

        if (tempo != 1)
            stretcher.Reset(tempo);
    }

    private double positionUnlocked()
    {
        if (sessionTempo == 1)
            return inner.PositionMs;

        return sessionStartMs + sessionOutputFrames * sessionTempo / sampleRate * 1000.0;
    }

    public void Dispose() => inner.Dispose();
}
