// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Audio;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Platform;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Configurations;

public class FrameworkConfigManager : ConfigManager<FrameworkSetting>
{
    /// <param name="storage">Where <c>framework.ini</c> is kept.</param>
    /// <param name="appType">Picks the defaults that differ between a game and an application.</param>
    public FrameworkConfigManager(Storage storage, AppType appType = AppType.Game)
        : this(storage, FramePacing.Current(appType))
    {
    }

    /// <param name="storage">Where <c>framework.ini</c> is kept.</param>
    /// <param name="pacing">Picks the defaults that differ between a game and an application, and on a phone.</param>
    internal FrameworkConfigManager(Storage storage, FramePacing pacing) : base(storage)
    {
        initializeDefaults(pacing);
    }

    private void initializeDefaults(FramePacing pacing)
    {
        Get(FrameworkSetting.FrameLimiter, pacing.DefaultFrameLimiter);
        Get(FrameworkSetting.ShowFpsGraph, PerformanceOverlayState.Hidden);
        Get(FrameworkSetting.ExecutionMode, ExecutionMode.MultiThread);
        Get(FrameworkSetting.MasterVolume, 1.0);
        Get(FrameworkSetting.TrackVolume, 1.0);
        Get(FrameworkSetting.SampleVolume, 1.0);
        Get(FrameworkSetting.AudioBackend, AudioBackend.Automatic);
        // A phone's output takes audio in bursts of its own size (0 asks for that); a buffer that doesn't line up
        // with them keeps the stream off the low-latency path.
        Get(FrameworkSetting.AudioDeviceBufferFrames, pacing.Mobile ? 0 : SDLAudioManager.DEFAULT_DEVICE_BUFFER_FRAMES);
        // A game on a phone hides the system bars, so an edge swipe in play only shows them rather than leaving; an
        // application keeps them, like the phone's other apps.
        Get(FrameworkSetting.WindowMode, pacing.Mobile && pacing.AppType == AppType.Game ? WindowMode.Borderless : WindowMode.Windowed);
        Get(FrameworkSetting.HardwareAcceleration, true);
        Get(FrameworkSetting.VideoZeroCopy, true);
        Get(FrameworkSetting.RendererType, RendererType.Automatic);
        Get(FrameworkSetting.WindowX, -1);
        Get(FrameworkSetting.WindowY, -1);
        Get(FrameworkSetting.WindowWidth, -1);
        Get(FrameworkSetting.WindowHeight, -1);
        Get(FrameworkSetting.RelativeMouseMode, false);
        Get(FrameworkSetting.CursorSensitivity, 1.0);
        Get(FrameworkSetting.SafeAreaPreview, SafeAreaPreview.Off);
        Get(FrameworkSetting.ShowLogOverlay, false);
        // Empty for the system's default output, which follows the system as it changes.
        Get(FrameworkSetting.AudioDevice, string.Empty);
    }
}
