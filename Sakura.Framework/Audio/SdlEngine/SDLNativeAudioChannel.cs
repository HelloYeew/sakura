// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A handle onto one voice inside <c>libsakura-audio</c>, presented as an <see cref="IAudioChannel"/>.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="SDLAudioChannel"/>, and deliberately the same public surface: the
/// difference is where the audio is mixed. Nothing here touches a sample. Gain, pan and rate are
/// single atomic stores the native mixer reads once per block; play, stop and seek are commands
/// applied in order on the audio thread; and the audio itself is either PCM the native engine holds
/// or a ring a <see cref="NativeStreamFeeder"/> fills from the decode thread.
/// </para>
/// <para>
/// The one thing the native side cannot do is raise a managed event, so an ended or looped voice
/// publishes a counter and <see cref="PollEvents"/> turns it into <see cref="OnEnd"/> on the update
/// thread. That is the same place the managed mixer's queued events are raised, so the two behave
/// alike from a caller's point of view.
/// </para>
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal class SDLNativeAudioChannel : ISDLChannel, IDuckable
{
    public event Action OnStart = () => { };
    public event Action OnStop = () => { };
    public event Action OnEnd = () => { };

    public event Action? Disposed;

    public ReactiveBool IsRunning { get; } = new ReactiveBool();

    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);

    /// <summary>
    /// Pitch-preserving speed, stretched by the <see cref="NativeStreamFeeder"/> on its way into the
    /// ring. A no-op on a sample, whose shared buffer is mixed straight out of native memory, as on the
    /// BASS backend.
    /// </summary>
    public Reactive<double> Tempo { get; } = new Reactive<double>(1.0);

    public bool AutoDispose { get; set; }

    private bool looping;

    public bool Looping
    {
        get => looping;
        set
        {
            looping = value;
            publishLoop();
        }
    }

    private double restartPoint;

    public double RestartPoint
    {
        get => restartPoint;
        set
        {
            restartPoint = value;
            publishLoop();
        }
    }

    protected readonly SakuraAudioEngine Engine;

    /// <summary>
    /// The manager, for the one thing a voice cannot know on its own: how far behind the mix the
    /// device is.
    /// </summary>
    protected readonly ISDLAudioContext Context;

    private readonly OutputLatencyCompensator latency = new OutputLatencyCompensator();

    /// <summary>
    /// The native node this channel drives.
    /// </summary>
    internal uint Node { get; }

    /// <summary>
    /// The decode side of a streaming voice, or null for one playing a shared PCM buffer.
    /// </summary>
    private readonly NativeStreamFeeder? feeder;

    /// <summary>
    /// Created on the first amplitude read. Most channels are hitsounds nobody visualises, and the
    /// reader carries two 256-bin arrays — on every channel, that was 2 KB of garbage per hitsound.
    /// </summary>
    private NativeAmplitudeReader? amplitudes;

    private SDLLowPassFilter? filter;

    private volatile bool isDisposed;

    protected bool IsDisposed => isDisposed;

    /// <summary>
    /// Where a streaming voice's position is measured from. The native side counts frames since the
    /// last seek, because a ring buffer has no idea where in the song it sits; a voice over a shared
    /// buffer reports an absolute cursor and needs no base at all.
    /// </summary>
    private double baseMs;

    /// <summary>
    /// The position to report, and the seek epoch it is waiting on, while a posted seek has not been
    /// applied yet. Without this a read in that window pairs the old cursor with the new base.
    /// </summary>
    private double seekTargetMs;

    /// <summary>
    /// The epoch a posted seek is waiting to see change, or -1 when none is outstanding.
    /// </summary>
    /// <remarks>
    /// Not zero: a voice that has never been seeked reports epoch 0, so a zero here would read as a
    /// seek to 0 that never lands, and the position would sit at zero for the whole track.
    /// </remarks>
    private long seekPostedAtEpoch = -1;

    private long lastEndEpoch;

    /// <summary>
    /// Held over what's posted to move or start the voice: from the update thread, from the audio thread looping it,
    /// and from the decode thread once a moved stream has its audio.
    /// </summary>
    private readonly Lock transport = new Lock();

    /// <summary>
    /// Whether a streaming voice was moved and its feeder doesn't have the new position's audio in yet. It's kept
    /// stopped until then: playing on, it would play what the ring still held of the old position, counted as the new
    /// one's, so the position would run ahead of what's heard by that much for the rest of the song.
    /// </summary>
    private bool awaitingSeekAudio;

    /// <summary>
    /// Whether to start the voice once <see cref="awaitingSeekAudio"/> clears.
    /// </summary>
    private bool playOnceFilled;

    /// <summary>
    /// Whether a stopped stream's decoder still has to be rewound before it plays (see <see cref="Stop"/>).
    /// </summary>
    private bool rewindOnPlay;

    /// <summary>
    /// Counts the seeks handed to the feeder, so a report about an earlier one is told apart.
    /// </summary>
    private int seekGeneration;

    /// <summary>
    /// The tempo asked for, and the one the audio since the last seek was stretched at. Every ring frame
    /// played is this many frames of the song.
    /// </summary>
    private double requestedTempo = 1;

    private double ringTempo = 1;

    public double Length { get; }

    public double OutputLatency => Context.OutputLatencyMs;

    public SDLNativeAudioChannel(ISDLAudioContext context, SakuraAudioEngine engine, uint node, double lengthMs, NativeStreamFeeder? feeder = null)
    {
        Context = context;
        Engine = engine;
        Node = node;
        Length = lengthMs;
        this.feeder = feeder;

        if (feeder != null)
            feeder.SeekFilled = onSeekFilled;

        IsRunning.ValueChanged += e =>
        {
            var handler = e.NewValue ? OnStart : OnStop;
            Context.RaiseEvent(() => handler?.Invoke());
        };

        Volume.ValueChanged += _ => publishGain();
        Balance.ValueChanged += _ => publishGain();
        Frequency.ValueChanged += e => Engine.SetRate(Node, e.NewValue);
        Tempo.ValueChanged += e => applyTempo(e.NewValue);

        publishGain();
    }

    private void applyTempo(double tempo)
    {
        if (isDisposed || feeder == null)
            return;

        tempo = Math.Clamp(tempo, TimeStretcher.MIN_TEMPO, TimeStretcher.MAX_TEMPO);

        if (tempo == requestedTempo)
            return;

        // The ring holds up to half a second already stretched at the old speed, so rather than let
        // that play out, start again from where the listener is: a seek in place, at the new speed.
        double position = CurrentTime;

        requestedTempo = tempo;
        feeder.SetTempo(tempo);
        seekInternal(position);
    }

    private void publishGain()
    {
        // The pan law lives here rather than in the native engine, next to the BASS backend's, so the
        // two can be compared: linear, full on one side at the extreme, both sides unity at centre.
        double balance = Math.Clamp(Balance.Value, -1.0, 1.0);

        float left = (float)(balance <= 0 ? 1.0 : 1.0 - balance);
        float right = (float)(balance >= 0 ? 1.0 : 1.0 + balance);

        Engine.SetGain(Node, (float)(Math.Max(0, Volume.Value) * ducking), left, right);
    }

    private double ducking = 1;

    public double Ducking
    {
        get => ducking;
        set
        {
            if (value == ducking)
                return;

            ducking = value;
            publishGain();
        }
    }

    private void publishLoop()
    {
        // A streaming voice cannot wrap itself: only this side can seek its decoder. It is still told
        // that it loops, so that it publishes the end and keeps running rather than stopping.
        Engine.SetLoop(Node, looping, millisecondsToFrames(restartPoint));
    }

    private long millisecondsToFrames(double milliseconds) =>
        (long)(Math.Max(0, milliseconds) / 1000.0 * Engine.SampleRate);

    #region Filter

    /// <summary>
    /// The low-pass insert attached to this channel, if any.
    /// </summary>
    internal SDLLowPassFilter? Filter => filter;

    /// <summary>
    /// Attaches a low-pass filter, replacing any existing one.
    /// </summary>
    /// <remarks>
    /// The filter object is the managed one, used purely as a coefficient calculator: it is where the
    /// cutoff maths lives and where it is tested, and the native voice applies whatever it publishes.
    /// </remarks>
    internal SDLLowPassFilter AttachLowPassFilter()
    {
        filter?.Dispose();

        SDLLowPassFilter? attached = null;
        attached = new SDLLowPassFilter(Engine.SampleRate, Engine.Channels, () => detachFilter(attached!));

        attached.CoefficientsChanged += publishFilter;

        var (enabled, coefficients) = attached.CurrentCoefficients;
        publishFilter(enabled, coefficients);

        filter = attached;
        return attached;
    }

    private void publishFilter(bool enabled, SDLLowPassFilter.Coefficients coefficients)
    {
        if (isDisposed)
            return;

        Engine.SetFilter(Node, enabled, coefficients.B0, coefficients.B1, coefficients.B2, coefficients.A1, coefficients.A2);
    }

    /// <summary>
    /// Turns the voice's filter off with the managed one disposed. The voice would otherwise go on filtering at the
    /// last cutoff published, which is wherever a sweep back up was when its owner let go: a duck released on a slow
    /// frame left the music muffled until the song changed.
    /// </summary>
    private void detachFilter(SDLLowPassFilter disposed)
    {
        if (isDisposed || filter != disposed)
            return;

        filter = null;
        Engine.SetFilter(Node, false, 1, 0, 0, 0, 0);
    }

    #endregion

    #region Transport

    public virtual void Play()
    {
        if (isDisposed)
            return;

        lock (transport)
        {
            // Replaying something that already finished should start it over rather than sit at the end
            // producing silence. A shared buffer rewinds itself inside the engine; a stream cannot, since
            // rewinding it means rewinding a decoder. Not one just moved somewhere, which is still marked
            // ended until the move lands: that's where it was asked to play from. A stopped stream's
            // decoder is rewound now too (see Stop).
            if (feeder != null && !awaitingSeekAudio && (rewindOnPlay || (Engine.TryGetState(Node, out var state) && state.Ended != 0)))
                seekInternal(rewindOnPlay ? 0 : Looping ? RestartPoint : 0);

            if (awaitingSeekAudio)
                playOnceFilled = true;
            else
                Engine.Play(Node);
        }

        IsRunning.Value = true;

        Context.WakeDecoder();
    }

    public virtual void Stop()
    {
        if (isDisposed)
            return;

        lock (transport)
        {
            playOnceFilled = false;

            // Matches the BASS backend, where Stop rewinds and Pause does not. The engine rewinds its cursor; a stream's
            // decoder is rewound when it next plays, or it would carry on from where it stopped while the position
            // counted from the start. Not here: a stop is often followed by disposing, and the decoding would be wasted.
            long epoch = currentSeekEpoch();
            Engine.Stop(Node);
            armSeekReport(0, epoch);
            rewindOnPlay = feeder != null;
        }

        IsRunning.Value = false;
    }

    public virtual void Pause()
    {
        if (isDisposed)
            return;

        lock (transport)
        {
            playOnceFilled = false;
            Engine.Pause(Node);
        }

        IsRunning.Value = false;
    }

    /// <summary>
    /// Moves both halves of the voice: the decoder, where there is one, and the engine's own cursor
    /// and DSP state.
    /// </summary>
    private void seekInternal(double milliseconds)
    {
        lock (transport)
        {
            rewindOnPlay = false;

            // A playing stream stops until the feeder has the new position's audio (see awaitingSeekAudio), and starts
            // again then. Before the seek, so the engine never mixes the old audio after its count was reset.
            if (feeder != null && (IsRunning.Value || playOnceFilled))
            {
                Engine.Pause(Node);
                playOnceFilled = true;
            }

            // Order matters. The engine's seek resets the interpolation window, the filter's delay line
            // and the metering, and zeroes its frame count; the feeder then discards what is buffered and
            // decodes from the new position. Doing it the other way round would let the engine's reset
            // land on audio the feeder had already written. The epoch is read before posting: read after,
            // a callback applying the seek in between would leave the position on the target for good.
            long epoch = currentSeekEpoch();
            Engine.Seek(Node, millisecondsToFrames(milliseconds));
            armSeekReport(milliseconds, epoch);

            seekFeeder(milliseconds);
            amplitudes?.Reset();
        }

        // A seek empties the ring, so every block until the decode thread refills it is silence out
        // of a running voice. Without this the seek waits out the scheduler's idle delay before it is
        // even noticed, and a second one before the flush the first pass posted has been acknowledged.
        Context.WakeDecoder();
    }

    /// <summary>
    /// Has the feeder decode from <paramref name="milliseconds"/>, the voice waiting for that audio to play.
    /// </summary>
    private void seekFeeder(double milliseconds)
    {
        if (feeder == null)
            return;

        awaitingSeekAudio = true;
        feeder.Seek(milliseconds, ++seekGeneration);
    }

    /// <summary>
    /// The feeder has the audio of the position a seek moved to in the ring. On the decode thread.
    /// </summary>
    private void onSeekFilled(int generation)
    {
        lock (transport)
        {
            if (isDisposed || generation != seekGeneration)
                return;

            awaitingSeekAudio = false;

            if (!playOnceFilled)
                return;

            playOnceFilled = false;
            Engine.Play(Node);
        }
    }

    private long currentSeekEpoch() => Engine.TryGetState(Node, out var state) ? state.SeekEpoch : 0;

    /// <summary>
    /// Records where the position is being moved to, and the seek epoch that was current when it was
    /// asked for, so <see cref="CurrentTime"/> can report the target until the engine confirms it.
    /// </summary>
    /// <param name="milliseconds">The position.</param>
    /// <param name="epoch">The voice's seek epoch from before the seek was posted.</param>
    private void armSeekReport(double milliseconds, long epoch)
    {
        latency.Reset();
        baseMs = feeder != null ? Math.Max(0, milliseconds) : 0;
        ringTempo = feeder != null ? requestedTempo : 1;
        seekTargetMs = Math.Max(0, milliseconds);
        seekPostedAtEpoch = epoch;
    }

    public double CurrentTime
    {
        get
        {
            if (isDisposed || !Engine.TryGetState(Node, out var state))
                return 0;

            // Still waiting on the audio thread: the cursor it is reporting belongs to the position we
            // are leaving, so answer with the one we are going to, as it's reported once there. Nothing of
            // it is heard until the output's latency has passed, so that's taken off as it will be then:
            // reported whole now, the position would step back by the latency as the seek landed.
            if (state.SeekEpoch == seekPostedAtEpoch)
                return latency.Compensate(seekTargetMs, Context.OutputLatencyMs, Frequency.Value * ringTempo);

            double raw = baseMs + state.SourceFrames / (double)Engine.SampleRate * 1000.0 * ringTempo;

            return latency.Compensate(raw, Context.OutputLatencyMs, Frequency.Value * ringTempo);
        }
        set
        {
            if (isDisposed)
                return;

            seekInternal(value);
        }
    }

    #endregion

    public float AmplitudeLeft => CurrentAmplitudes.AmplitudeLeft;

    public float AmplitudeRight => CurrentAmplitudes.AmplitudeRight;

    public virtual ChannelAmplitudes CurrentAmplitudes
    {
        get
        {
            if (isDisposed || !IsRunning.Value)
                return ChannelAmplitudes.Empty;

            return (amplitudes ??= new NativeAmplitudeReader()).Read(Engine, Node);
        }
    }

    /// <summary>
    /// Turns anything the voice has signalled into events, on the update thread.
    /// </summary>
    public void PollEvents()
    {
        if (isDisposed || !Engine.TryGetState(Node, out var state))
            return;

        if (state.EndEpoch == lastEndEpoch)
            return;

        lastEndEpoch = state.EndEpoch;

        // A voice over a shared buffer wraps inside the engine without anything here seeking it, so
        // this is the only notice the compensator gets that the position jumped.
        latency.Reset();

        var ended = OnEnd;
        Context.RaiseEvent(() => ended?.Invoke());

        if (Looping)
        {
            // A voice over a shared buffer has already wrapped itself inside the engine and is still
            // playing; a stream is sitting on an empty ring waiting to be told where to go.
            if (feeder != null)
                seekInternal(RestartPoint);

            return;
        }

        // Ended: a start waiting on a seek's audio would play a voice that reports itself stopped.
        lock (transport)
            playOnceFilled = false;

        IsRunning.Value = false;

        if (AutoDispose)
            Dispose();
    }

    public virtual void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        if (filter != null)
        {
            filter.CoefficientsChanged -= publishFilter;
            filter.Dispose();
            filter = null;
        }

        // The writer goes first. Disposing the feeder waits out any decode pass in flight and refuses
        // every later one, so once it returns nothing writes into this voice's ring again. The other
        // order left a window where the decode thread had already resolved the voice, the device
        // callback retired it, maintain() freed the ring, and the write landed in freed memory.
        feeder?.Dispose();

        // The engine unlinks the voice from the graph on its own thread and only then marks the slot
        // reclaimable, so nothing here has to wait for a callback to finish.
        Engine.DestroyNode(Node);

        IsRunning.Value = false;
        OnStart = null!;
        OnStop = null!;
        OnEnd = null!;

        var disposed = Disposed;
        Disposed = null;
        disposed?.Invoke();
    }
}
