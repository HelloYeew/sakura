// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// A dummy implementation of <see cref="IAudioChannel"/>, simulate playback.
/// </summary>
internal abstract class AudioChannel : IAudioChannel
{
    public event Action OnStart = () => { };
    public event Action OnStop = () => { };
    public event Action OnEnd = () => { };

    public ReactiveBool IsRunning { get; } = new ReactiveBool();
    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);

    private double currentTime;
    public double CurrentTime
    {
        get => currentTime;
        set => currentTime = Math.Clamp(value, 0, Length);
    }

    public double Length { get; protected set; }
    public bool Looping { get; set; }

    protected readonly AudioManager Manager;
    private bool isPaused;
    public bool IsPaused => isPaused;

    protected AudioChannel(AudioManager manager)
    {
        Manager = manager;
        IsRunning.ValueChanged += isRunningChanged;
    }

    private void isRunningChanged(ValueChangedEvent<bool> e)
    {
        if (e.NewValue)
        {
            // IsRunning changed to true
            isPaused = false;
            Manager.AddChannel(this);
            OnStart?.Invoke();
        }
        else
        {
            // IsRunning changed to false
            Manager.RemoveChannel(this);
            OnStop?.Invoke();
        }
    }

    public virtual void Play()
    {
        if (IsRunning.Value && !isPaused)
            CurrentTime = 0; // Restart if already playing

        IsRunning.Value = true;
    }

    public virtual void Stop()
    {
        CurrentTime = 0;
        isPaused = false;
        IsRunning.Value = false; // This will trigger OnStop via the event handler
    }

    public virtual void Pause()
    {
        if (!IsRunning.Value)
            return;

        isPaused = true;
        IsRunning.Value = false;
    }

    /// <summary>
    /// Internal update called by AudioManager.
    /// </summary>
    /// <param name="frameTime">Elapsed time in milliseconds.</param>
    public virtual void Update(double frameTime)
    {
        if (!IsRunning.Value || isPaused)
            return;

        double rate = Frequency.Value;
        if (rate <= 0)
            return;

        CurrentTime += frameTime * rate;

        if (CurrentTime >= Length)
        {
            OnEnd?.Invoke();
            HandleLoop();
        }
    }

    protected abstract void HandleLoop();

    public virtual void Dispose()
    {
        Stop();
        OnStart = null;
        OnStop = null;
        OnEnd = null;
    }
}
