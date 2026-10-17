// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A mixer node inside <c>libsakura-audio</c>, presented as an <see cref="IAudioMixer"/>.
/// </summary>
/// <remarks>
/// A native mixer sums its children and then applies its own gain, filter, and metering exactly as a
/// voice does, which is what preserves <see cref="BassEngine.BassAudioMixer"/>'s semantics. SDL's own
/// device mixing is flat, so routing everything straight at the device would have left
/// <see cref="IAudioManager.TrackMixer"/> and <see cref="IAudioManager.SampleMixer"/> as bookkeeping
/// with no per-mixer volume, filter, or spectrum.
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SDLNativeAudioMixer : SDLNativeAudioChannel, ISDLMixer
{
    private readonly Lock sync = new Lock();
    private readonly List<IAudioChannel> channels = new List<IAudioChannel>();

    /// <summary>
    /// Immutable view of <see cref="channels"/>, or null when membership has changed since it was
    /// last built.
    /// </summary>
    /// <remarks>
    /// Rebuilt on the first read after a change, not on the change itself. Every hitsound adds itself
    /// here and removes itself a moment later, so rebuilding on mutation copied the whole list twice
    /// per hitsound for a view that only <see cref="Graphics.Performance.AudioMixerVisualiser"/>
    /// reads, every 100 ms, while it is open. Not copied on every read either, which would add the
    /// same churn from the other side.
    /// </remarks>
    private volatile IAudioChannel[]? snapshot = Array.Empty<IAudioChannel>();

    private IAudioChannel[] currentSnapshot()
    {
        var current = snapshot;

        if (current != null)
            return current;

        lock (sync)
            return snapshot ??= channels.ToArray();
    }

    public SDLNativeAudioMixer(ISDLAudioContext context, SakuraAudioEngine engine, uint node)
        : base(context, engine, node, 0)
    {
    }

    /// <summary>
    /// The channels routed into this mixer.
    /// </summary>
    /// <remarks>
    /// Returns an immutable snapshot, so listing it is safe without external locking and cannot
    /// throw if a channel is added or removed mid-iteration. The BASS backend instead hands out its
    /// live backing list and happens to work because callers lock the same object — this does not
    /// reproduce that arrangement.
    /// </remarks>
    public IEnumerable<IAudioChannel> ActiveChannels => currentSnapshot();

    public void AddChannel(IAudioChannel channel)
    {
        if (channel is not SDLNativeAudioChannel native)
            return;

        lock (sync)
        {
            if (channels.Contains(channel))
                return;

            // The graph edge is the engine's; this list exists only so the visualiser has something to
            // enumerate, since the native graph is not walkable from here.
            if (!Engine.AddChild(Node, native.Node))
                return;

            channels.Add(channel);
            snapshot = null;
        }
    }

    public void RemoveChannel(IAudioChannel channel)
    {
        lock (sync)
        {
            if (!channels.Remove(channel))
                return;

            if (channel is SDLNativeAudioChannel native)
                Engine.RemoveChild(Node, native.Node);

            snapshot = null;
        }
    }

    /// <summary>
    /// The number of children currently producing audio.
    /// </summary>
    public int RunningChannelCount
    {
        get
        {
            var current = currentSnapshot();
            int count = 0;

            foreach (var channel in current)
            {
                if (channel.IsRunning.Value)
                    count++;
            }

            return count;
        }
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
