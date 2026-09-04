// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.Headless;

public class HeadlessAudioChannel : IAudioChannel
{
    private readonly HeadlessAudioManager? manager;

    public HeadlessAudioChannel(double length, HeadlessAudioManager? manager = null)
    {
        Length = length;
        this.manager = manager;

        IsRunning.ValueChanged += e =>
        {
            var handler = e.NewValue ? OnStart : OnStop;
            raiseEvent(() => handler?.Invoke());
        };
    }

    private void raiseEvent(Action action)
    {
        if (manager != null)
            manager.RaiseEvent(action);
        else
            action();
    }

    public void Play()
    {
        if (Length > 0)
            IsRunning.Value = true;
    }

    public void Stop()
    {
        IsRunning.Value = false;
        CurrentTime = 0;
    }

    public void Pause()
    {
        IsRunning.Value = false;
    }

    public event Action? OnStart;
    public event Action? OnStop;
    public event Action? OnEnd;
    public ReactiveBool IsRunning { get; } = new ReactiveBool();
    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);
    public Reactive<double> Tempo { get; } = new Reactive<double>(1.0);
    public double Length { get; private set; }
    public bool Looping { get; set; }
    public bool AutoDispose { get; set; }

    public double RestartPoint { get; set; } = 0;
    public float AmplitudeLeft => 0;
    public float AmplitudeRight => 0;

    public ChannelAmplitudes CurrentAmplitudes => ChannelAmplitudes.Empty;

    private double currentTime;
    public double CurrentTime
    {
        get => currentTime;
        set => currentTime = Math.Clamp(value, 0, Length);
    }

    public void Dispose()
    {
        IsRunning.Value = false;
        OnStart = null;
        OnStop = null;
        OnEnd = null;
    }

    /// <summary>
    /// Simulate the channel update.
    /// </summary>
    /// <param name="elapsedMs">The elapsed time in milliseconds since the last update.</param>
    internal void Update(double elapsedMs)
    {
        if (!IsRunning.Value) return;

        CurrentTime += elapsedMs * Frequency.Value;

        if (CurrentTime >= Length)
        {
            if (Looping)
            {
                CurrentTime = RestartPoint;
            }
            else
            {
                CurrentTime = Length;
                IsRunning.Value = false;

                var ended = OnEnd;
                raiseEvent(() => ended?.Invoke());
            }
        }
    }
}
