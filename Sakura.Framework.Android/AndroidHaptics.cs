// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Android.Content;
using Android.OS;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

// A normal permission, granted with the install: nothing is asked of the player.
[assembly: Android.App.UsesPermission(Android.Manifest.Permission.Vibrate)]

namespace Sakura.Framework.Android;

/// <summary>
/// The vibrator, through the system's predefined effects (API 29), which each phone tunes to its own motor: a tick, a
/// click, a heavy click. As touch feedback, so the player's own touch vibration setting turns it off too (API 33).
/// </summary>
internal sealed class AndroidHaptics : IPlatformHaptics
{
    // How long a tap of a set strength lasts, on a motor that can vary its strength: about one of the predefined clicks.
    private const long tap_milliseconds = 12;

    private readonly Vibrator? vibrator;

    // Logged once: a vibrator that refuses one effect refuses them all.
    private bool failed;

    public AndroidHaptics(Context context)
    {
        vibrator = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? (context.GetSystemService(Context.VibratorManagerService) as VibratorManager)?.DefaultVibrator
            : getLegacyVibrator(context);

        IsSupported = vibrator?.HasVibrator ?? false;
    }

#pragma warning disable CA1422 // The way to it below API 31.
    private static Vibrator? getLegacyVibrator(Context context) => context.GetSystemService(Context.VibratorService) as Vibrator;
#pragma warning restore CA1422

    public bool IsSupported { get; }

    public void Play(HapticFeedback feedback) => vibrate(VibrationEffect.CreatePredefined(feedback switch
    {
        HapticFeedback.Selection or HapticFeedback.Light => VibrationEffect.EffectTick,
        HapticFeedback.Medium or HapticFeedback.Success => VibrationEffect.EffectClick,
        HapticFeedback.Warning => VibrationEffect.EffectDoubleClick,
        _ => VibrationEffect.EffectHeavyClick
    }));

    public void PlayTap(float intensity, float sharpness)
    {
        if (!IsSupported)
            return;

        intensity = Math.Clamp(intensity, 0, 1);

        // A motor that can vary its strength plays it as asked; one that can't takes the predefined tap nearest it.
        // Neither can vary the sharpness.
        vibrate(vibrator!.HasAmplitudeControl
            ? VibrationEffect.CreateOneShot(tap_milliseconds, Math.Max(1, (int)Math.Round(intensity * 255)))
            : VibrationEffect.CreatePredefined(intensity < 0.4f ? VibrationEffect.EffectTick : intensity < 0.8f ? VibrationEffect.EffectClick : VibrationEffect.EffectHeavyClick));
    }

    private void vibrate(VibrationEffect? effect)
    {
        if (!IsSupported || effect == null)
            return;

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                vibrator!.Vibrate(effect, VibrationAttributes.CreateForUsage((int)VibrationAttributesUsageType.Touch));
            else
                vibrator!.Vibrate(effect);
        }
        catch (Exception e)
        {
            if (failed)
                return;

            failed = true;
            Logger.Error("The vibrator refused feedback; there'll be none.", e);
        }
    }
}
