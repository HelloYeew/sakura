// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using Sakura.Framework.Reactive;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Audio;

/// <summary>
/// Main audio engine interface. Responsible for loading audio data and managing all playbacks.
/// </summary>
public interface IAudioManager
{
    /// <summary>
    /// Master volume for all audio playbacks, affect both tracks and samples.
    /// </summary>
    Reactive<double> MasterVolume { get; }

    /// <summary>
    /// Master volume for track playbacks that separately from samples but still affect by <see cref="MasterVolume"/>
    /// </summary>
    Reactive<double> TrackVolume { get; }

    /// <summary>
    /// Master volume for sample playbacks that separately from tracks but still affect by <see cref="MasterVolume"/>
    /// </summary>
    Reactive<double> SampleVolume { get; }

    /// <summary>
    /// The output devices there are to play on, by name, as the system lists them. The system's default output isn't
    /// among them: it's always there, as the empty name in <see cref="OutputDevice"/>. Changes as devices are plugged
    /// in and out, raising <see cref="OutputDevicesChanged"/>. Empty where the system picks the output itself, as
    /// Android does, so the default is the only choice there is.
    /// </summary>
    IReadOnlyList<string> OutputDevices { get; }

    /// <summary>
    /// Raised through <see cref="EventScheduler"/> when <see cref="OutputDevices"/> changes.
    /// </summary>
    event Action OutputDevicesChanged;

    /// <summary>
    /// The device to play on, by its name in <see cref="OutputDevices"/>, or empty for the system's default, which
    /// follows the system as its default changes. A device that isn't there plays on the default until it's plugged
    /// in again, and then moves back to it.
    /// </summary>
    Reactive<string> OutputDevice { get; }

    /// <summary>
    /// The device the sound plays on now, by name: the one picked in <see cref="OutputDevice"/>, or whichever the
    /// system's default is while that's followed. Where the system picks the output itself, as Android does, it's what
    /// the platform says of it ("Bluetooth: Galaxy Buds"). Empty where nothing says, or nothing plays.
    /// </summary>
    string PlayingDevice { get; }

    /// <summary>
    /// Raised through <see cref="EventScheduler"/> when <see cref="PlayingDevice"/> changes: the device picked was
    /// plugged in or out, or the system's default moved to another device.
    /// </summary>
    event Action PlayingDeviceChanged;

    /// <summary>
    /// Whether the system has the output for something else for now, such as a call or an alarm. Until it's given
    /// back, tracks stand still where they are and samples play silently, so the music picks up where it was and
    /// nothing played meanwhile sounds all at once afterwards. An app's host sets it from what the platform says
    /// (<see cref="Platform.HostOptions.TakesAudioFocus"/>); a game can watch it to pause itself.
    /// </summary>
    Reactive<bool> OutputInterrupted { get; }

    /// <summary>
    /// Get the master mixer for track playbacks. All track playbacks will be routed through this mixer.
    /// </summary>
    IAudioMixer TrackMixer { get; }

    /// <summary>
    /// Get the master mixer for sample playbacks. All sample playbacks will be routed through this mixer.
    /// </summary>
    IAudioMixer SampleMixer { get; }

    /// <summary>
    /// Delay between audio being mixed and reached the speakers, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Defaults to 0 for backends that do not measure it. It is a real device figure where a backend
    /// does report one, so treat 0 as "unknown" rather than "instant".
    /// </remarks>
    double OutputLatencyMs => 0;

    /// <summary>
    /// Whether playing, stopping or seeking a channel only takes effect at the next <see cref="Update"/>, so how often
    /// that runs adds to how late a sound starts.
    /// </summary>
    /// <remarks>
    /// True unless a backend says otherwise: one that hands those straight to its mixer only raises end-of-sound
    /// events and tidies up in <see cref="Update"/>, which can run far less often.
    /// </remarks>
    bool PlaybackWaitsForUpdate => true;

    /// <summary>
    /// Loads a track from a <see cref="Stream"/>
    /// </summary>
    /// <param name="stream">The stream to load from</param>
    /// <returns>The loaded <see cref="ITrack"/></returns>
    ITrack CreateTrack(Stream stream);

    /// <summary>
    /// Loads a sample from a <see cref="Stream"/>
    /// </summary>
    /// <param name="stream">The stream to load from</param>
    /// <returns>The loaded <see cref="ISample"/></returns>
    ISample CreateSample(Stream stream);

    /// <summary>
    /// Load a track from a precised file path
    /// </summary>
    /// <param name="path">The full path to the audio file</param>
    /// <returns>The loaded <see cref="ITrack"/></returns>
    ITrack CreateTrackFromFile(string path);

    /// <summary>
    /// Enqueues an action to be executed safely on the Audio thread.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    void EnqueueAction(Action action);

    /// <summary>
    /// The scheduler used for invoking publicly exposed delegate events.
    /// </summary>
    Scheduler? EventScheduler { get; set; }

    /// <summary>
    /// Raises a user-facing event through <see cref="EventScheduler"/>, or inline if there is none.
    /// </summary>
    void RaiseEvent(Action action)
    {
        if (EventScheduler != null)
            EventScheduler.Add(action);
        else
            action();
    }

    /// <summary>
    /// Load a sample from a precised file path
    /// </summary>
    /// <param name="path">The full path to the audio file</param>
    /// <returns>The loaded <see cref="ISample"/></returns>
    ISample CreateSampleFromFile(string path);

    /// <summary>
    /// Updates the state of all playing audio channels. Called once per audio-thread frame by
    /// <see cref="Platform.AppHost.PerformSoundUpdate"/>, and never from the update thread.
    /// </summary>
    /// <remarks>
    /// This is where every queued audio action runs, so it is the thread every backend call is
    /// serialized onto. User-facing events raised from here are marshaled by
    /// <see cref="EventScheduler"/> rather than fired inline.
    /// </remarks>
    /// <param name="frameTime">Elapsed time since the previous audio frame, in milliseconds.</param>
    void Update(double frameTime);

    /// <summary>
    /// Instantly stops all playing tracks and samples.
    /// </summary>
    void StopAll();
}
