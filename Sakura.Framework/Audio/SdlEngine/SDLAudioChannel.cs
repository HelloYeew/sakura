// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// SDL implementation of <see cref="IAudioChannel"/> — one playing voice.
/// </summary>
/// <remarks>
/// <para>
/// The signal path, in order: pull from the <see cref="IPcmSource"/> through the rate resampler,
/// apply the low-pass insert if one is attached, apply volume and pan, meter, then add into the
/// caller's buffer. Filtering happens before gain so that automating volume does not change the
/// filter's behavior.
/// </para>
/// <para>
/// Threading: <see cref="Fill"/> runs on the mix thread; everything else is called from the update
/// thread. Values the mix loop needs are cached into plain fields as they change, so the audio path
/// never reads a <see cref="Reactive{T}"/>, and user-visible events are marshaled to the audio
/// thread through <see cref="ISDLAudioContext.EnqueueAction"/> exactly as the BASS backend does.
/// </para>
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal class SDLAudioChannel : IAudioChannel
{
    public event Action OnStart = () => { };
    public event Action OnStop = () => { };
    public event Action OnEnd = () => { };

    /// <summary>
    /// Raised once this channel has released its source, on the audio thread. Whatever created the
    /// channel uses it to drop the reference, keeping the underlying audio data alive.
    /// </summary>
    internal event Action? Disposed;

    public ReactiveBool IsRunning { get; } = new ReactiveBool();

    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);

    /// <summary>
    /// Pitch-preserving speed. A no-op on this backend, as <see cref="IAudioChannel.Tempo"/> permits
    /// for backends without a tempo DSP — a WSOLA implementation is a later item.
    /// </summary>
    public Reactive<double> Tempo { get; } = new Reactive<double>(1.0);

    public bool AutoDispose { get; set; }

    public bool Looping { get; set; }

    public double RestartPoint { get; set; }

    protected readonly ISDLAudioContext Context;

    private readonly IPcmSource? source;
    private readonly CubicResampler? resampler;
    private readonly AmplitudeTap tap = new AmplitudeTap();

    /// <summary>
    /// Scratch for one block of this voice's own output before it is summed into the destination.
    /// Grown on demand and then reused, so a steady-state mix allocates nothing.
    /// </summary>
    private float[] scratch = Array.Empty<float>();

    // Snapshots of the reactive properties, refreshed on change so the mix thread reads plain
    // fields. Assignments to these types are atomic, so a torn read is not possible.
    private volatile float volume = 1f;
    private volatile float gainLeft = 1f;
    private volatile float gainRight = 1f;
    private double frequency = 1.0;

    private SDLLowPassFilter? filter;

    private volatile bool isDisposed;

    /// <summary>
    /// Set on the mix thread when the source runs out, so the end is handled once on the audio
    /// thread rather than repeatedly from inside the mix loop.
    /// </summary>
    private bool endHandled;

    public SDLAudioChannel(ISDLAudioContext context, IPcmSource? source)
    {
        Context = context;
        this.source = source;

        if (source != null)
            resampler = new CubicResampler(context.Channels);

        IsRunning.ValueChanged += e =>
        {
            if (e.NewValue) OnStart?.Invoke();
            else OnStop?.Invoke();
        };

        Volume.ValueChanged += e => volume = (float)Math.Max(0, e.NewValue);
        Balance.ValueChanged += _ => updateBalance();
        Frequency.ValueChanged += e => frequency = e.NewValue;

        updateBalance();
    }

    private void updateBalance()
    {
        // Linear pan, matching the BASS backend's ChannelAttribute.Pan: full on one side at the
        // extreme, both sides unity at centre.
        double balance = Math.Clamp(Balance.Value, -1.0, 1.0);

        gainLeft = (float)(balance <= 0 ? 1.0 : 1.0 - balance);
        gainRight = (float)(balance >= 0 ? 1.0 : 1.0 + balance);
    }

    /// <summary>
    /// The low-pass insert attached to this channel, if any.
    /// </summary>
    internal SDLLowPassFilter? Filter => filter;

    /// <summary>
    /// Attaches a low-pass filter, replacing any existing one.
    /// </summary>
    internal SDLLowPassFilter AttachLowPassFilter()
    {
        filter?.Dispose();

        var attached = new SDLLowPassFilter(Context.SampleRate, Context.Channels);
        filter = attached;
        return attached;
    }

    /// <summary>
    /// Mixes this channel's contribution for one block <b>additively</b> into
    /// <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// Additive rather than overwriting: a mixer sums its children into one buffer, so the caller
    /// zeroes the destination once and every child adds to it. A channel that is stopped, disposed,
    /// or has nothing decoded yet contributes nothing and leaves the buffer untouched.
    /// </remarks>
    public virtual void Fill(Span<float> destination)
    {
        if (isDisposed || !IsRunning.Value || source == null || resampler == null)
            return;

        int channels = Context.Channels;
        int frames = destination.Length / channels;

        if (frames <= 0)
            return;

        if (scratch.Length < frames * channels)
            scratch = new float[frames * channels];

        var block = scratch.AsSpan(0, frames * channels);
        int produced = resampler.Read(source, block, frames, frequency);

        if (produced < frames)
        {
            // Nothing more is coming from this source for now. Whether that is the end of the audio
            // or a decoder that has fallen behind is the source's call, not ours.
            block.Slice(produced * channels).Clear();

            if (source.Ended)
                handleEnd();
        }

        if (produced > 0)
        {
            var produsedBlock = block.Slice(0, produced * channels);

            filter?.Process(produsedBlock);
            applyGain(produsedBlock, channels);
            tap.Feed(produsedBlock);

            for (int i = 0; i < produsedBlock.Length; i++)
                destination[i] += produsedBlock[i];
        }
    }

    private void applyGain(Span<float> block, int channels)
    {
        float left = volume * gainLeft;
        float right = volume * gainRight;

        if (channels == 2)
        {
            for (int i = 0; i + 1 < block.Length; i += 2)
            {
                block[i] *= left;
                block[i + 1] *= right;
            }

            return;
        }

        // Panning is only meaningful in stereo; anything else just takes the volume.
        for (int i = 0; i < block.Length; i++)
            block[i] *= volume;
    }

    /// <summary>
    /// Called from the mix thread the moment the source runs dry. Loops immediately so the wrap is
    /// as tight as the decoder allows, and defers everything user-visible to the audio thread.
    /// </summary>
    private void handleEnd()
    {
        if (endHandled)
            return;

        endHandled = true;

        bool looping = Looping;

        if (looping)
        {
            seekInternal(RestartPoint);
            endHandled = false;
        }

        Context.EnqueueAction(() =>
        {
            OnEnd?.Invoke();

            if (looping)
                return;

            IsRunning.Value = false;

            if (AutoDispose)
                Dispose();
        });
    }

    public virtual void Play()
    {
        if (isDisposed)
            return;

        Context.EnqueueAction(() =>
        {
            if (isDisposed)
                return;

            // Replaying something that already finished should start it over rather than sit at the
            // end producing silence.
            if (source?.Ended == true)
                seekInternal(Looping ? RestartPoint : 0);

            endHandled = false;
            IsRunning.Value = true;
            Context.WakeDecoder();
        });
    }

    public virtual void Stop()
    {
        if (isDisposed)
            return;

        Context.EnqueueAction(() =>
        {
            if (isDisposed)
                return;

            IsRunning.Value = false;

            // Matches the BASS backend, where Stop rewinds and Pause does not.
            seekInternal(0);
            endHandled = false;
        });
    }

    public virtual void Pause()
    {
        if (isDisposed)
            return;

        Context.EnqueueAction(() =>
        {
            if (isDisposed)
                return;

            IsRunning.Value = false;
        });
    }

    /// <summary>
    /// Moves the source and clears every piece of state that holds audio from the old position:
    /// the interpolation window, the filter's delay line, and the metering capture.
    /// </summary>
    private void seekInternal(double milliseconds)
    {
        source?.Seek(milliseconds);
        resampler?.Reset();
        filter?.ClearState();
        tap.Reset();
        Context.WakeDecoder();
    }

    public double CurrentTime
    {
        get => isDisposed ? 0 : source?.PositionMs ?? 0;
        set
        {
            if (isDisposed)
                return;

            Context.EnqueueAction(() =>
            {
                if (isDisposed)
                    return;

                seekInternal(value);
                endHandled = false;
            });
        }
    }

    public double Length => source?.LengthMs ?? 0;

    public float AmplitudeLeft => CurrentAmplitudes.AmplitudeLeft;

    public float AmplitudeRight => CurrentAmplitudes.AmplitudeRight;

    public virtual ChannelAmplitudes CurrentAmplitudes
    {
        get
        {
            if (isDisposed || !IsRunning.Value)
                return ChannelAmplitudes.Empty;

            return tap.Read();
        }
    }

    public virtual void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        Context.EnqueueAction(() =>
        {
            // Only once the mix thread can no longer be inside Fill for this channel, which the
            // isDisposed check above guarantees by the time this runs on the audio thread.
            filter?.Dispose();
            filter = null;

            source?.Dispose();

            IsRunning.Value = false;
            OnStart = null!;
            OnStop = null!;
            OnEnd = null!;

            var disposed = Disposed;
            Disposed = null;
            disposed?.Invoke();
        });
    }
}
