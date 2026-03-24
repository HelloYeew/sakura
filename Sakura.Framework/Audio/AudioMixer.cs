// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio;

/// <summary>
/// A base or dummy implementation of an audio mixer.
/// </summary>
public class AudioMixer : IAudioMixer
{
    protected readonly List<IAudioChannel> MixedChannels = new List<IAudioChannel>();

    public event Action OnStart = delegate { };
    public event Action OnStop = delegate { };
    public event Action OnEnd = delegate { };

    public ReactiveBool IsRunning { get; } = new ReactiveBool();
    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);

    public double CurrentTime { get; set; }
    public double Length => 0;
    public double RestartPoint { get; set; }
    public float AmplitudeLeft { get; } = 0;
    public float AmplitudeRight { get; } = 0;
    public bool Looping { get; set; }
    public bool AutoDispose { get; set; }

    public virtual void Play()
    {
        IsRunning.Value = true;
        OnStart.Invoke();
    }

    public virtual void Pause()
    {
        IsRunning.Value = false;
        OnStop.Invoke();
    }

    public virtual void Stop()
    {
        IsRunning.Value = false;
        CurrentTime = 0;
        OnStop.Invoke();
    }

    public IEnumerable<IAudioChannel> ActiveChannels => MixedChannels;

    public virtual void AddChannel(IAudioChannel channel)
    {
        if (!MixedChannels.Contains(channel))
            MixedChannels.Add(channel);
    }

    public virtual void RemoveChannel(IAudioChannel channel)
    {
        MixedChannels.Remove(channel);
    }

    public virtual void Dispose()
    {
        MixedChannels.Clear();
    }
}
