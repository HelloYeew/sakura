// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Changes the speed of interleaved PCM without changing its pitch, by WSOLA (waveform-similarity
/// overlap-add). This is what <see cref="IAudioChannel.Tempo"/> runs on in the SDL engines.
/// </summary>
/// <remarks>
/// <para>
/// The input is cut into sequences a few tens of milliseconds long. Each one is copied to the output
/// as it is, so within a sequence the audio plays at its own pitch; what changes the speed is how far
/// the input moves on between sequences, which is <see cref="Tempo"/> times what the output does.
/// Jumping through the input like that would click at every joint, so each sequence starts with a
/// short crossfade from the end of the previous one, and the next sequence is not taken from exactly
/// where the arithmetic says but from the nearby point whose waveform lines up best with what it is
/// fading out of. That search is the "waveform similarity" part, and it is why this sounds like the
/// music slowing down rather than a stutter.
/// </para>
/// <para>
/// The parameters are SoundTouch's (the library BASS_FX's tempo is built on): an 8 ms crossfade,
/// and a sequence length and search range that shrink as the tempo rises, so a slowed-down track
/// does not repeat itself in audible chunks and a sped-up one does not skip whole notes.
/// </para>
/// <para>
/// The output stays in step with the input: output frame <c>n</c> carries the input from around
/// frame <c>n × tempo</c>, and the total output length is the input's divided by the tempo. That is
/// what lets a caller report a position as <c>start + framesPlayed × tempo</c>. "Around" is inherent
/// to WSOLA: inside a sequence the audio plays at its own speed, so it drifts from the nominal
/// position by up to a sequence × |1 − tempo| before the next joint pulls it back. The input is
/// offset so that drift is centred, half a sequence × |1 − tempo| (plus half a search range) either
/// way, rather than always running ahead. Not thread-safe; one thread pushes and pulls.
/// </para>
/// </remarks>
internal sealed class TimeStretcher
{
    /// <summary>
    /// The crossfade between sequences. Long enough to hide the joint, short enough not to smear a
    /// transient across it.
    /// </summary>
    private const double overlap_ms = 8;

    /// <summary>
    /// The tempo range SoundTouch fits its automatic sequence and search lengths over. Tempos outside
    /// it use the length at the nearer end.
    /// </summary>
    private const double auto_tempo_low = 0.5;

    private const double auto_tempo_high = 2.0;

    private const double sequence_ms_at_low = 125;
    private const double sequence_ms_at_high = 50;

    private const double seek_ms_at_low = 25;
    private const double seek_ms_at_high = 15;

    /// <summary>
    /// The tempo range accepted, matching what the BASS backend clamps to at the low end. Far past
    /// either end WSOLA has nothing left to work with and every sequence is a joint.
    /// </summary>
    public const double MIN_TEMPO = 0.05;

    public const double MAX_TEMPO = 20;

    private readonly int sampleRate;
    private readonly int channels;
    private readonly int overlapFrames;

    private double tempo = 1;
    private int sequenceFrames;
    private int seekFrames;
    private double nominalSkip;
    private double skipFract;

    /// <summary>
    /// Set until the first sequence has gone out. That one has nothing to crossfade from and nothing to
    /// line up with, so it is taken from exactly the start of the input and fades in from silence.
    /// </summary>
    private bool beginning = true;

    private bool flushed;

    private long inputFramesTotal;
    private long outputFramesTotal;

    private float[] input = Array.Empty<float>();
    private int inputStart;
    private int inputFrames;

    private float[] output = Array.Empty<float>();
    private int outputStart;
    private int outputFrames;

    /// <summary>
    /// The last <see cref="overlapFrames"/> of the previous sequence, which the next one fades in over.
    /// </summary>
    private readonly float[] mid;

    /// <summary>
    /// <see cref="mid"/> folded to mono and weighted towards its centre: what the search lines the
    /// candidates up against.
    /// </summary>
    private readonly float[] reference;

    /// <summary>
    /// The search region folded to mono, rebuilt for each sequence.
    /// </summary>
    private float[] monoScratch = Array.Empty<float>();

    public TimeStretcher(int sampleRate, int channels)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        this.sampleRate = sampleRate;
        this.channels = channels;

        overlapFrames = Math.Max(8, framesFor(overlap_ms));
        mid = new float[overlapFrames * channels];
        reference = new float[overlapFrames];

        Reset(1);
    }

    /// <summary>
    /// The speed this instance is running at: input frames consumed per output frame produced.
    /// </summary>
    public double Tempo => tempo;

    /// <summary>
    /// Frames produced and waiting for <see cref="ReceiveOutput"/>.
    /// </summary>
    public int AvailableFrames => outputFrames;

    /// <summary>
    /// How many more input frames the next sequence needs before it can be produced. A caller pulling
    /// from a source can ask for exactly this rather than guessing a chunk size.
    /// </summary>
    public int InputFramesWanted => Math.Max(0, sampleRequirement - inputFrames);

    /// <summary>
    /// Whether <see cref="Flush"/> has been called and everything it produced has been received:
    /// there is nothing more to come out of this instance until the next <see cref="Reset"/>.
    /// </summary>
    public bool Drained => flushed && outputFrames == 0;

    /// <summary>
    /// Discards all buffered audio and starts again at <paramref name="newTempo"/>, as for a seek.
    /// </summary>
    public void Reset(double newTempo)
    {
        tempo = Math.Clamp(newTempo, MIN_TEMPO, MAX_TEMPO);

        // SoundTouch's automatic lengths: a straight line between the two ends of the fitted range.
        double t = Math.Clamp((tempo - auto_tempo_low) / (auto_tempo_high - auto_tempo_low), 0, 1);

        sequenceFrames = Math.Max(2 * overlapFrames + 1, framesFor(sequence_ms_at_low + (sequence_ms_at_high - sequence_ms_at_low) * t));
        seekFrames = Math.Max(1, framesFor(seek_ms_at_low + (seek_ms_at_high - seek_ms_at_low) * t));
        nominalSkip = tempo * (sequenceFrames - overlapFrames);

        skipFract = 0;
        beginning = true;
        flushed = false;

        inputStart = inputFrames = 0;
        outputStart = outputFrames = 0;
        inputFramesTotal = outputFramesTotal = 0;

        Array.Clear(mid);
        Array.Clear(reference);

        ensureCapacity(ref input, (sampleRequirement + 4096) * channels);
        ensureCapacity(ref output, (sequenceFrames + 4096) * channels);

        if (monoScratch.Length < seekFrames + overlapFrames)
            monoScratch = new float[seekFrames + overlapFrames];
    }

    /// <summary>
    /// Appends interleaved frames to the input and produces every sequence they complete.
    /// </summary>
    public void PutInput(ReadOnlySpan<float> interleaved)
    {
        if (flushed)
            throw new InvalidOperationException($"{nameof(TimeStretcher)} was flushed; reset it before adding more input.");

        int frames = interleaved.Length / channels;

        if (frames <= 0)
            return;

        appendInput(interleaved.Slice(0, frames * channels));
        inputFramesTotal += frames;

        process();
    }

    /// <summary>
    /// Marks the end of the input, and produces what is left of it. The output then ends where the
    /// input's would at this tempo, rather than on a whole number of sequences.
    /// </summary>
    public void Flush()
    {
        if (flushed)
            return;

        flushed = true;

        long expected = (long)Math.Round(inputFramesTotal / tempo);

        // Silence behind the real tail, so the last sequences can complete, then cut the output back
        // to the length the input actually earns.
        while (outputFramesTotal < expected)
        {
            int padding = Math.Max(1, sampleRequirement - inputFrames);
            ensureCapacity(ref input, (inputStart + inputFrames + padding) * channels);
            input.AsSpan((inputStart + inputFrames) * channels, padding * channels).Clear();
            inputFrames += padding;

            process();
        }

        long excess = outputFramesTotal - expected;

        if (excess > 0)
        {
            int trimmed = (int)Math.Min(excess, outputFrames);
            outputFrames -= trimmed;
            outputFramesTotal -= trimmed;
        }

        inputStart = inputFrames = 0;
    }

    /// <summary>
    /// Takes up to <paramref name="maxFrames"/> produced frames into <paramref name="destination"/>.
    /// </summary>
    /// <returns>Frames written.</returns>
    public int ReceiveOutput(Span<float> destination, int maxFrames)
    {
        int frames = Math.Min(Math.Min(maxFrames, outputFrames), destination.Length / channels);

        if (frames <= 0)
            return 0;

        output.AsSpan(outputStart * channels, frames * channels).CopyTo(destination);

        outputStart += frames;
        outputFrames -= frames;

        if (outputFrames == 0)
            outputStart = 0;

        return frames;
    }

    /// <summary>
    /// What one sequence needs in hand: the furthest the search can reach plus a whole sequence from
    /// there, or the skip to the next one plus its crossfade, whichever is further.
    /// </summary>
    private int sampleRequirement => Math.Max((int)Math.Ceiling(nominalSkip) + overlapFrames, sequenceFrames) + seekFrames;

    private void process()
    {
        while (inputFrames >= sampleRequirement)
        {
            int offset = beginning || tempo == 1 ? 0 : seekBestOverlap();

            produceSequence(offset);

            if (beginning)
            {
                beginning = false;

                // Two things put the audio ahead of frame n × tempo on average, and the input starts
                // that much behind to cancel them. Every later sequence starts somewhere in [0, seek)
                // past where the arithmetic puts it, half a search range on average; and within a
                // sequence the audio runs at its own speed, so by its middle it has drifted half a
                // sequence × (1 − tempo) from where it should be: ahead when slowed down, behind when
                // sped up, which this term's sign takes care of.
                if (tempo != 1)
                    skipFract -= seekFrames / 2.0 + (sequenceFrames - overlapFrames) / 2.0 * (1 - tempo);
            }

            skipFract += nominalSkip;

            int skip = 0;

            if (skipFract >= 1)
            {
                skip = (int)skipFract;
                skipFract -= skip;
            }

            skip = Math.Min(skip, inputFrames);
            inputStart += skip;
            inputFrames -= skip;
        }

        // Slide the unread input back to the front once it has moved far enough along for the copy to
        // be worth it, so the buffer stays bounded however long the stream runs.
        if (inputStart > 0 && inputStart >= inputFrames)
        {
            input.AsSpan(inputStart * channels, inputFrames * channels).CopyTo(input);
            inputStart = 0;
        }
    }

    /// <summary>
    /// Writes one sequence taken from <paramref name="offset"/> frames into the input: the crossfade
    /// out of the previous sequence, then the body, keeping its tail as the next crossfade.
    /// </summary>
    private void produceSequence(int offset)
    {
        int produced = sequenceFrames - overlapFrames;

        if (outputFrames == 0)
            outputStart = 0;
        else if (outputStart > 0 && (outputStart + outputFrames + produced) * channels > output.Length)
        {
            output.AsSpan(outputStart * channels, outputFrames * channels).CopyTo(output);
            outputStart = 0;
        }

        ensureCapacity(ref output, (outputStart + outputFrames + produced) * channels);

        var source = input.AsSpan((inputStart + offset) * channels, sequenceFrames * channels);
        var destination = output.AsSpan((outputStart + outputFrames) * channels, produced * channels);

        // Linear crossfade, arriving fully on the new sequence at the end of the overlap. At the very
        // start the previous sequence is silence, which makes this a short fade-in: no click after a seek.
        float inverse = 1f / overlapFrames;

        for (int frame = 0; frame < overlapFrames; frame++)
        {
            float fadeIn = frame * inverse;
            float fadeOut = 1f - fadeIn;

            for (int c = 0; c < channels; c++)
            {
                int i = frame * channels + c;
                destination[i] = source[i] * fadeIn + mid[i] * fadeOut;
            }
        }

        int bodyFrames = sequenceFrames - 2 * overlapFrames;

        source.Slice(overlapFrames * channels, bodyFrames * channels)
              .CopyTo(destination.Slice(overlapFrames * channels));

        source.Slice((sequenceFrames - overlapFrames) * channels, overlapFrames * channels).CopyTo(mid);

        updateReference();

        outputFrames += produced;
        outputFramesTotal += produced;
    }

    /// <summary>
    /// Folds <see cref="mid"/> to mono and weights it with a parabola peaking mid-overlap, as SoundTouch
    /// does: the middle of the crossfade is where a mismatch is loudest, so it should count most.
    /// </summary>
    private void updateReference()
    {
        for (int frame = 0; frame < overlapFrames; frame++)
        {
            float sum = 0;

            for (int c = 0; c < channels; c++)
                sum += mid[frame * channels + c];

            reference[frame] = sum * frame * (overlapFrames - frame);
        }
    }

    /// <summary>
    /// The offset into the input, within the search range, whose start best continues
    /// <see cref="mid"/>: the highest normalised cross-correlation, with a mild preference for the
    /// middle of the range so the search does not drift to one edge and stay there.
    /// </summary>
    private int seekBestOverlap()
    {
        int length = seekFrames + overlapFrames;
        var mono = monoScratch.AsSpan(0, length);
        var region = input.AsSpan(inputStart * channels, length * channels);

        for (int frame = 0; frame < length; frame++)
        {
            float sum = 0;

            for (int c = 0; c < channels; c++)
                sum += region[frame * channels + c];

            mono[frame] = sum;
        }

        var weights = reference.AsSpan();

        // The candidate's energy, slid along one frame at a time rather than summed afresh.
        double energy = 0;

        for (int i = 0; i < overlapFrames; i++)
            energy += mono[i] * mono[i];

        int best = 0;
        double bestScore = double.MinValue;

        for (int offset = 0; offset < seekFrames; offset++)
        {
            if (offset > 0)
            {
                float leaving = mono[offset - 1];
                float entering = mono[offset + overlapFrames - 1];
                energy += entering * entering - leaving * leaving;
            }

            double correlation = dot(weights, mono.Slice(offset, overlapFrames));
            double score = correlation / Math.Sqrt(Math.Max(energy, 1e-9));

            // SoundTouch's centring heuristic: a slight penalty towards either end of the range.
            double fromCentre = (2.0 * offset - seekFrames) / seekFrames;
            score = (score + 0.1) * (1 - 0.25 * fromCentre * fromCentre);

            if (score > bestScore)
            {
                bestScore = score;
                best = offset;
            }
        }

        return best;
    }

    private static double dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int i = 0;
        float sum = 0;

        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            var va = MemoryMarshal.Cast<float, Vector<float>>(a);
            var vb = MemoryMarshal.Cast<float, Vector<float>>(b.Slice(0, a.Length));
            var acc = Vector<float>.Zero;

            for (int v = 0; v < va.Length; v++)
                acc += va[v] * vb[v];

            sum = Vector.Dot(acc, Vector<float>.One);
            i = va.Length * Vector<float>.Count;
        }

        for (; i < a.Length; i++)
            sum += a[i] * b[i];

        return sum;
    }

    private void appendInput(ReadOnlySpan<float> interleaved)
    {
        int frames = interleaved.Length / channels;

        if (inputStart > 0 && (inputStart + inputFrames + frames) * channels > input.Length)
        {
            input.AsSpan(inputStart * channels, inputFrames * channels).CopyTo(input);
            inputStart = 0;
        }

        ensureCapacity(ref input, (inputStart + inputFrames + frames) * channels);
        interleaved.CopyTo(input.AsSpan((inputStart + inputFrames) * channels));
        inputFrames += frames;
    }

    private static void ensureCapacity(ref float[] buffer, int floats)
    {
        if (buffer.Length >= floats)
            return;

        Array.Resize(ref buffer, Math.Max(floats, buffer.Length * 2));
    }

    private int framesFor(double milliseconds) => (int)(milliseconds / 1000.0 * sampleRate);
}
