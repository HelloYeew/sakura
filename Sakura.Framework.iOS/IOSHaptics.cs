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

    // A tap's strength and sharpness are each kept to a twentieth, so that taps alike share a player.
    private const int steps = 20;

    // Players are made once for each strength and sharpness and started again each time: each holds one of the engine's
    // channels for as long as it lives, and the engine has about 250. Past this many, they're all let go and made again
    // as they're needed.
    private const int max_players = 32;

    private readonly Dictionary<int, ICHHapticPatternPlayer> players = new Dictionary<int, ICHHapticPatternPlayer>();

    // Logged once until a player can be made again, rather than for every tap refused.
    private bool playerFailed;

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

        (float intensity, float sharpness) = feedback switch
        {
            HapticFeedback.Selection => (0.35f, 0.9f),
            HapticFeedback.Light => (0.45f, 0.6f),
            HapticFeedback.Medium => (0.7f, 0.5f),
            _ => (1f, 0.4f)
        };

        tap(intensity, sharpness);
    }

    public void PlayTap(float intensity, float sharpness)
    {
        if (IsSupported)
            tap(intensity, sharpness);
    }

    private void tap(float intensity, float sharpness)
    {
        int strengthStep = (int)MathF.Round(Math.Clamp(intensity, 0, 1) * steps);
        int sharpnessStep = (int)MathF.Round(Math.Clamp(sharpness, 0, 1) * steps);
        int key = strengthStep * (steps + 1) + sharpnessStep;

        lock (engineLock)
        {
            if (!ensureRunning())
                return;

            // 0 is now (CHHapticTimeImmediate).
            if (players.TryGetValue(key, out var player) && player.Start(0, out _))
                return;

            // One that won't start any more is made again.
            if (player != null)
            {
                players.Remove(key);
                player.Dispose();
            }

            player = makePlayer((float)strengthStep / steps, (float)sharpnessStep / steps);

            if (player == null)
                return;

            players[key] = player;
            player.Start(0, out _);
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
                    releasePlayers();
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
    private ICHHapticPatternPlayer? makePlayer(float intensity, float sharpness)
    {
        if (players.Count >= max_players)
            releasePlayers();

        var player = createPlayer(intensity, sharpness, out NSError? error);

        // The engine's channels may have run out some other way: once more, with every player let go.
        if (player == null && players.Count > 0)
        {
            releasePlayers();
            player = createPlayer(intensity, sharpness, out error);
        }

        if (player == null && !playerFailed)
            Logger.Error($"Could not make a haptic player: {error?.LocalizedDescription}");

        playerFailed = player == null;
        return player;
    }

    // Under the lock, with the engine running.
    private ICHHapticPatternPlayer? createPlayer(float intensity, float sharpness, out NSError? error)
    {
        var transient = new CHHapticEvent(CHHapticEventType.HapticTransient,
        [
            new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, intensity),
            new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, sharpness)
        ], 0);

        var pattern = new CHHapticPattern([transient], Array.Empty<CHHapticDynamicParameter>(), out error);

        if (error != null)
            return null;

        // The engine hands the player over autoreleased. The framework's threads drain a pool every frame, but a tap can
        // come from any thread, and a .NET thread's own pool drains only when the thread ends: without a pool here, the
        // player would live on, holding its channel.
        using (new NSAutoreleasePool())
        {
            return engine!.CreatePlayer(pattern, out error);
        }
    }

    // Under the lock. A player let go frees its channel.
    private void releasePlayers()
    {
        foreach (var player in players.Values)
            player.Dispose();

        players.Clear();
    }

    private void notify(UINotificationFeedbackType type) => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
    {
        notifications ??= new UINotificationFeedbackGenerator();
        notifications.NotificationOccurred(type);
    });
}
