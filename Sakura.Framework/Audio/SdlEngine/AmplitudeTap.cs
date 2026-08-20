// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A metering tap on one point in the mix graph: the mix thread pushes the audio passing through it,
/// and the update thread pulls a <see cref="ChannelAmplitudes"/> snapshot for visualizers.
/// </summary>
/// <remarks>
/// Behavior is matched to <see cref="BassEngine.BassAudioChannel"/>, see that class for original behavior
/// </remarks>
internal sealed class AmplitudeTap
{
    /// <summary>
    /// Minimum interval between recomputing. Matches the BASS backend and means several visualizers
    /// reading the same tap in one frame share a single transform.
    /// </summary>
    private const long cache_interval_ms = 15;

    // Per-frame temporal damping of the spectrum so visualizers receive a smooth signal rather than
    // the raw, jittery FFT. Each refresh eases the stored value toward the new reading, retaining
    // this fraction of the old value per ~60fps frame. Copied from BassAudioChannel, including the
    // framerate-independence: the retained fraction shrinks as more time passes.
    private const double amplitude_retain_per_frame = 0.4;
    private const double amplitude_reference_frame_ms = 1000.0 / 60.0;

    /// <summary>
    /// Frames folded into one peak-hold segment, and how many segments the peak window spans.
    /// 64 x 16 = 1024 frames: about 21ms at 48kHz, 23ms at 44.1kHz, the same order as the 20ms
    /// window BASS reports a level over.
    /// </summary>
    private const int peak_segment_frames = 64;
    private const int peak_segment_count = 16;

    private readonly Lock sync = new Lock();

    private readonly AudioFft fft = new AudioFft();

    /// <summary>
    /// The most recent <see cref="AudioFft.FFT_SIZE"/> mono samples, as a circular buffer.
    /// </summary>
    private readonly float[] capture = new float[AudioFft.FFT_SIZE];

    private int captureWritePosition;

    /// <summary>
    /// Whether anything has been fed since the last <see cref="Reset"/>. Until then the capture
    /// window is silence, and a visualizer should see an empty spectrum rather than a decaying one.
    /// </summary>
    private bool hasAudio;

    /// <summary>
    /// The L/R peak window, as a ring of per-segment peaks: <see cref="peak_segment_frames"/> frames
    /// are folded into one segment, and the reported peak is the largest of the last
    /// <see cref="peak_segment_count"/> of them.
    /// </summary>
    /// <remarks>
    /// The window is measured in audio, not in reader frames, and that is the whole point.
    /// The mix thread does not feed at a steady rate: the device asks for its buffer's worth in one
    /// go (1024 frames is typical), so the tap see burst and suddenly silent since it's immediately.
    /// To try to make it behave like BASS's 20ms <c>BASS_ChannelGetLevel</c>.
    /// </remarks>
    private readonly float[] segmentPeaksLeft = new float[peak_segment_count];
    private readonly float[] segmentPeaksRight = new float[peak_segment_count];

    private int segmentIndex;
    private int segmentFrames;

    private readonly float[] scratch = new float[AudioFft.FFT_SIZE];
    private readonly float[] rawBins = new float[AudioFft.BIN_COUNT];
    private readonly float[] dampedBins = new float[AudioFft.BIN_COUNT];

    private long lastReadTick;
    private ChannelAmplitudes cached = ChannelAmplitudes.Empty;

    /// <summary>
    /// The peak level of the left channel over the audio window preceding the last <see cref="Read"/>.
    /// </summary>
    public float AmplitudeLeft { get; private set; }

    /// <summary>
    /// The peak level of the right channel over the audio window preceding the last <see cref="Read"/>.
    /// </summary>
    public float AmplitudeRight { get; private set; }

    /// <summary>
    /// Records a block of interleaved stereo audio passing through this point.
    /// </summary>
    /// <remarks>
    /// Peaks are held for a fixed span of audio rather than until the next <see cref="Read"/>, so what
    /// a reader sees does not depend on how its polling happens to line up with the mix thread's
    /// bursts. See <see cref="segmentPeaksLeft"/>.
    /// </remarks>
    public void Feed(ReadOnlySpan<float> interleavedStereo)
    {
        if (interleavedStereo.IsEmpty)
            return;

        lock (sync)
        {
            int segment = segmentIndex;
            int framesInSegment = segmentFrames;
            float peakLeft = segmentPeaksLeft[segment];
            float peakRight = segmentPeaksRight[segment];
            int position = captureWritePosition;

            for (int i = 0; i + 1 < interleavedStereo.Length; i += 2)
            {
                float left = interleavedStereo[i];
                float right = interleavedStereo[i + 1];

                float absoluteLeft = Math.Abs(left);
                float absoluteRight = Math.Abs(right);

                if (absoluteLeft > peakLeft) peakLeft = absoluteLeft;
                if (absoluteRight > peakRight) peakRight = absoluteRight;

                if (++framesInSegment == peak_segment_frames)
                {
                    segmentPeaksLeft[segment] = peakLeft;
                    segmentPeaksRight[segment] = peakRight;

                    // Move on to the oldest slot. Its stale contents stay readable until the store
                    // below, which is fine: it is about to be overwritten with this segment's peak,
                    // and until then it is one segment of history that has not quite expired.
                    segment = segment + 1 == peak_segment_count ? 0 : segment + 1;
                    framesInSegment = 0;
                    peakLeft = 0;
                    peakRight = 0;
                }

                // BASS folds both channels into one spectrum unless asked for individual FFTs, so
                // average rather than picking a side.
                capture[position] = (left + right) * 0.5f;
                position = position + 1 == capture.Length ? 0 : position + 1;
            }

            segmentPeaksLeft[segment] = peakLeft;
            segmentPeaksRight[segment] = peakRight;
            segmentIndex = segment;
            segmentFrames = framesInSegment;
            captureWritePosition = position;
            hasAudio = true;
        }
    }

    /// <summary>
    /// Returns the current amplitude snapshot, recomputing at most once per
    /// <see cref="cache_interval_ms"/>.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="ChannelAmplitudes"/> wraps a buffer this tap reuses, matching the BASS
    /// backend: callers must consume it before the next read rather than holding onto it.
    /// </remarks>
    public ChannelAmplitudes Read()
    {
        long now = Environment.TickCount64;
        long elapsed = now - lastReadTick;

        if (elapsed < cache_interval_ms)
            return cached;

        lastReadTick = now;

        bool any;

        lock (sync)
        {
            any = hasAudio;
            int start = captureWritePosition;

            if (any)
            {
                // Unwrap oldest-to-newest so the window the FFT sees is contiguous in time.
                int tail = capture.Length - start;
                capture.AsSpan(start, tail).CopyTo(scratch);
                capture.AsSpan(0, start).CopyTo(scratch.AsSpan(tail));
            }

            float peakLeft = 0;
            float peakRight = 0;

            for (int i = 0; i < peak_segment_count; i++)
            {
                if (segmentPeaksLeft[i] > peakLeft) peakLeft = segmentPeaksLeft[i];
                if (segmentPeaksRight[i] > peakRight) peakRight = segmentPeaksRight[i];
            }

            AmplitudeLeft = peakLeft;
            AmplitudeRight = peakRight;
        }

        if (!any)
        {
            Array.Clear(dampedBins);
            cached = new ChannelAmplitudes(0f, 0f, dampedBins);
            return cached;
        }

        fft.Compute(scratch, rawBins);

        // Ease each bin toward its new reading instead of snapping, retaining less of the old value
        // the more time has passed since the previous read.
        float retain = (float)Math.Pow(amplitude_retain_per_frame, elapsed / amplitude_reference_frame_ms);

        for (int i = 0; i < dampedBins.Length; i++)
            dampedBins[i] = rawBins[i] + (dampedBins[i] - rawBins[i]) * retain;

        cached = new ChannelAmplitudes(AmplitudeLeft, AmplitudeRight, dampedBins);
        return cached;
    }

    /// <summary>
    /// Drops all captured audio and metering state, so a stopped or seeked channel does not report a
    /// spectrum from where it used to be.
    /// </summary>
    public void Reset()
    {
        lock (sync)
        {
            Array.Clear(capture);
            captureWritePosition = 0;
            hasAudio = false;
            Array.Clear(segmentPeaksLeft);
            Array.Clear(segmentPeaksRight);
            segmentIndex = 0;
            segmentFrames = 0;
        }

        Array.Clear(dampedBins);
        AmplitudeLeft = 0;
        AmplitudeRight = 0;
        lastReadTick = 0;
        cached = ChannelAmplitudes.Empty;
    }
}
