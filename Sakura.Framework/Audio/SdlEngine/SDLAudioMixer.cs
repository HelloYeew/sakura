// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// SDL implementation of <see cref="IAudioMixer"/>
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SDLAudioMixer : SDLAudioChannel, IAudioMixer
{
    private readonly Lock sync = new Lock();
    private readonly List<IAudioChannel> channels = new List<IAudioChannel>();

    /// <summary>
    /// Immutable view of <see cref="channels"/>, rebuilt only when membership changes.
    /// </summary>
    /// <remarks>
    /// Rebuilt on mutation rather than copied on read because <see cref="ActiveChannels"/> is polled
    /// every frame by <see cref="Graphics.Performance.AudioMixerVisualiser"/>, and a per-read copy
    /// would add steady allocation churn to a backend whose whole premise is not provoking the GC.
    /// </remarks>
    private volatile IAudioChannel[] snapshot = Array.Empty<IAudioChannel>();

    /// <summary>
    /// Sum of this mixer's children for the current block, before its own inserts.
    /// </summary>
    private float[] scratch = Array.Empty<float>();

    public SDLAudioMixer(ISDLAudioContext context)
        : base(context, null)
    {
    }

    /// <summary>
    /// The channels routed into this mixer.
    /// </summary>
    /// <remarks>
    /// Returns an immutable snapshot, so enumerating it is safe without external locking and cannot
    /// throw if a channel is added or removed mid-iteration. The BASS backend instead hands out its
    /// live backing list and happens to work because callers lock the same object — this does not
    /// reproduce that arrangement.
    /// </remarks>
    public IEnumerable<IAudioChannel> ActiveChannels => snapshot;

    public void AddChannel(IAudioChannel channel)
    {
        if (channel is not SDLAudioChannel)
            return;

        lock (sync)
        {
            if (channels.Contains(channel))
                return;

            channels.Add(channel);
            snapshot = channels.ToArray();
        }
    }

    public void RemoveChannel(IAudioChannel channel)
    {
        lock (sync)
        {
            if (!channels.Remove(channel))
                return;

            snapshot = channels.ToArray();
        }
    }

    /// <summary>
    /// The number of children currently producing audio.
    /// </summary>
    public int RunningChannelCount
    {
        get
        {
            var current = snapshot;
            int count = 0;

            foreach (var channel in current)
            {
                if (channel.IsRunning.Value)
                    count++;
            }

            return count;
        }
    }

    public override void Fill(Span<float> destination)
    {
        if (IsDisposed || !IsRunning.Value)
            return;

        var current = snapshot;

        if (current.Length == 0)
            return;

        if (scratch.Length < destination.Length)
            scratch = new float[destination.Length];

        var block = scratch.AsSpan(0, destination.Length);
        block.Clear();

        foreach (var channel in current)
        {
            // Children add into the shared block; a stopped or starved one contributes nothing.
            if (channel is SDLAudioChannel sdlChannel)
                sdlChannel.Fill(block);
        }

        ApplyInsertsAndMix(block, destination);
    }

    public override void Dispose()
    {
        lock (sync)
        {
            channels.Clear();
            snapshot = Array.Empty<IAudioChannel>();
        }

        base.Dispose();
    }
}
