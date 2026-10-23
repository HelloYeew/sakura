// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using CoreHaptics;
using Foundation;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using UIKit;

namespace Sakura.Framework.iOS;

/// <summary>
/// The Taptic Engine through Core Haptics, which plays from any thread with little delay: the taps a game plays as notes
/// are hit can't wait for the main thread, as UIKit's feedback generators would. Success, warning and error are the
/// system's own patterns, through UIKit on the main thread, which needn't be on time to the millisecond.
/// </summary>
internal sealed class IOSHaptics : IPlatformHaptics
{
    private readonly object engineLock = new object();

    private CHHapticEngine? engine;
    private bool running;

    // A player for each kind, made once and started again each time it plays, rather than two new objects every tap.
    private readonly Dictionary<HapticFeedback, ICHHapticPatternPlayer?> players = new Dictionary<HapticFeedback, ICHHapticPatternPlayer?>();

    // Made and used on the main thread.
    private UINotificationFeedbackGenerator? notifications;

    public bool IsSupported { get; } = CHHapticEngine.GetHardwareCapabilities().SupportsHaptics;

    public void Prepare()
    {
        lock (engineLock)
            ensureRunning();
    }

    public void Play(HapticFeedback feedback)
    {
        if (!IsSupported)
            return;

        switch (feedback)
        {
            case HapticFeedback.Success:
                notify(UINotificationFeedbackType.Success);
                return;

            case HapticFeedback.Warning:
                notify(UINotificationFeedbackType.Warning);
                return;

            case HapticFeedback.Error:
                notify(UINotificationFeedbackType.Error);
                return;
        }

        lock (engineLock)
        {
            if (!ensureRunning())
                return;

            if (!players.TryGetValue(feedback, out var player))
            {
                (float intensity, float sharpness) = feedback switch
                {
                    HapticFeedback.Selection => (0.35f, 0.9f),
                    HapticFeedback.Light => (0.45f, 0.6f),
                    HapticFeedback.Medium => (0.7f, 0.5f),
                    _ => (1f, 0.4f)
                };

                players[feedback] = player = createPlayer(intensity, sharpness);
            }

            start(player);
        }
    }

    public void PlayTap(float intensity, float sharpness)
    {
        if (!IsSupported)
            return;

        lock (engineLock)
        {
            if (ensureRunning())
                start(createPlayer(intensity, sharpness));
        }
    }

    // Under the lock.
    private bool ensureRunning()
    {
        if (!IsSupported)
            return false;

        if (engine == null)
        {
            engine = new CHHapticEngine(out NSError? error);

            if (error != null)
            {
                Logger.Error($"Could not set up the haptic engine: {error.LocalizedDescription}");
                engine = null;
                return false;
            }

            // Haptics alone don't need the audio session, so they play whatever the session is doing, and the motor
            // powers down between uses.
            engine.PlaysHapticsOnly = true;
            engine.AutoShutdownEnabled = true;

            // The system stops it in the background, after an interruption, or idle; it starts again on the next tap. A
            // reset (the haptics server restarted) leaves the players made on it unusable.
            engine.StoppedHandler = _ =>
            {
                lock (engineLock)
                    running = false;
            };

            engine.ResetHandler = () =>
            {
                lock (engineLock)
                {
                    running = false;
                    players.Clear();
                }
            };
        }

        if (!running)
        {
            running = engine.Start(out NSError? error);

            if (!running)
                Logger.Error($"Could not start the haptic engine: {error?.LocalizedDescription}");
        }

        return running;
    }

    // Under the lock, with the engine running.
    private ICHHapticPatternPlayer? createPlayer(float intensity, float sharpness)
    {
        var tap = new CHHapticEvent(CHHapticEventType.HapticTransient,
        [
            new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, Math.Clamp(intensity, 0, 1)),
            new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, Math.Clamp(sharpness, 0, 1))
        ], 0);

        var pattern = new CHHapticPattern([tap], Array.Empty<CHHapticDynamicParameter>(), out NSError? error);

        if (error != null)
        {
            Logger.Error($"Could not make a haptic pattern: {error.LocalizedDescription}");
            return null;
        }

        var player = engine!.CreatePlayer(pattern, out error);

        if (error != null)
            Logger.Error($"Could not make a haptic player: {error.LocalizedDescription}");

        return player;
    }

    // 0 is now (CHHapticTimeImmediate).
    private static void start(ICHHapticPatternPlayer? player) => player?.Start(0, out _);

    private void notify(UINotificationFeedbackType type) => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
    {
        notifications ??= new UINotificationFeedbackGenerator();
        notifications.NotificationOccurred(type);
    });
}
