// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
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
internal class SDLNativeAudioChannel : ISDLChannel
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
    /// Pitch-preserving speed. A no-op on this backend, as <see cref="IAudioChannel.Tempo"/> permits
    /// for backends without a tempo DSP — a WSOLA implementation is a later item.
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

    private readonly NativeAmplitudeReader amplitudes = new NativeAmplitudeReader();

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

    public double Length { get; }

    public SDLNativeAudioChannel(ISDLAudioContext context, SakuraAudioEngine engine, uint node, double lengthMs, NativeStreamFeeder? feeder = null)
    {
        Context = context;
        Engine = engine;
        Node = node;
        Length = lengthMs;
        this.feeder = feeder;

        IsRunning.ValueChanged += e =>
        {
            if (e.NewValue) OnStart?.Invoke();
            else OnStop?.Invoke();
        };

        Volume.ValueChanged += _ => publishGain();
        Balance.ValueChanged += _ => publishGain();
        Frequency.ValueChanged += e => Engine.SetRate(Node, e.NewValue);

        publishGain();
    }

    private void publishGain()
    {
        // The pan law lives here rather than in the native engine, next to the BASS backend's, so the
        // two can be compared: linear, full on one side at the extreme, both sides unity at centre.
        double balance = Math.Clamp(Balance.Value, -1.0, 1.0);

        float left = (float)(balance <= 0 ? 1.0 : 1.0 - balance);
        float right = (float)(balance >= 0 ? 1.0 : 1.0 + balance);

        Engine.SetGain(Node, (float)Math.Max(0, Volume.Value), left, right);
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

        var attached = new SDLLowPassFilter(Engine.SampleRate, Engine.Channels);

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

    #endregion

    #region Transport

    public virtual void Play()
    {
        if (isDisposed)
            return;

        // Replaying something that already finished should start it over rather than sit at the end
        // producing silence. A shared buffer rewinds itself inside the engine; a stream cannot, since
        // rewinding it means rewinding a decoder.
        if (feeder != null && Engine.TryGetState(Node, out var state) && state.Ended != 0)
            seekInternal(Looping ? RestartPoint : 0);

        Engine.Play(Node);
        IsRunning.Value = true;
    }

    public virtual void Stop()
    {
        if (isDisposed)
            return;

        // Matches the BASS backend, where Stop rewinds and Pause does not.
        Engine.Stop(Node);
        armSeekReport(0);
        IsRunning.Value = false;
    }

    public virtual void Pause()
    {
        if (isDisposed)
            return;

        Engine.Pause(Node);
        IsRunning.Value = false;
    }

    /// <summary>
    /// Moves both halves of the voice: the decoder, where there is one, and the engine's own cursor
    /// and DSP state.
    /// </summary>
    private void seekInternal(double milliseconds)
    {
        // Order matters. The engine's seek resets the interpolation window, the filter's delay line
        // and the metering, and zeroes its frame count; the feeder then discards what is buffered and
        // decodes from the new position. Doing it the other way round would let the engine's reset
        // land on audio the feeder had already written.
        Engine.Seek(Node, millisecondsToFrames(milliseconds));
        armSeekReport(milliseconds);

        feeder?.Seek(milliseconds);
        amplitudes.Reset();
    }

    /// <summary>
    /// Records where the position is being moved to, and the seek epoch that was current when it was
    /// asked for, so <see cref="CurrentTime"/> can report the target until the engine confirms it.
    /// </summary>
    private void armSeekReport(double milliseconds)
    {
        latency.Reset();
        baseMs = feeder != null ? Math.Max(0, milliseconds) : 0;
        seekTargetMs = Math.Max(0, milliseconds);
        seekPostedAtEpoch = Engine.TryGetState(Node, out var state) ? state.SeekEpoch : 0;
    }

    public double CurrentTime
    {
        get
        {
            if (isDisposed || !Engine.TryGetState(Node, out var state))
                return 0;

            // Still waiting on the audio thread: the cursor it is reporting belongs to the position we
            // are leaving, so answer with the one we are going to. Uncompensated on purpose — nothing
            // of the new position has been mixed yet, let alone queued, so there is no latency to
            // subtract and subtracting one would report a seek as landing short.
            if (state.SeekEpoch == seekPostedAtEpoch)
                return seekTargetMs;

            double raw = baseMs + state.SourceFrames / (double)Engine.SampleRate * 1000.0;

            return latency.Compensate(raw, Context.OutputLatencyMs, Frequency.Value);
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

            return amplitudes.Read(Engine, Node);
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

        OnEnd?.Invoke();

        if (Looping)
        {
            // A voice over a shared buffer has already wrapped itself inside the engine and is still
            // playing; a stream is sitting on an empty ring waiting to be told where to go.
            if (feeder != null)
                seekInternal(RestartPoint);

            return;
        }

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

        // The engine unlinks the voice from the graph on its own thread and only then marks the slot
        // reclaimable, so nothing here has to wait for a callback to finish.
        Engine.DestroyNode(Node);

        feeder?.Dispose();

        IsRunning.Value = false;
        OnStart = null!;
        OnStop = null!;
        OnEnd = null!;

        var disposed = Disposed;
        Disposed = null;
        disposed?.Invoke();
    }
}
