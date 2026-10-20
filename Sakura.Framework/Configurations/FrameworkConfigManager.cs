// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Audio;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Platform;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Configurations;

public class FrameworkConfigManager : ConfigManager<FrameworkSetting>
{
    /// <param name="storage">Where <c>framework.ini</c> is kept.</param>
    /// <param name="appType">Picks the defaults that differ between a game and an application.</param>
    public FrameworkConfigManager(Storage storage, AppType appType = AppType.Game) : base(storage)
    {
        initializeDefaults(appType);
    }

    private void initializeDefaults(AppType appType)
    {
        // An application gains nothing from frames the display cannot show.
        Get(FrameworkSetting.FrameLimiter, appType == AppType.Game ? FrameSync.Limit2x : FrameSync.VSync);
        Get(FrameworkSetting.ShowFpsGraph, PerformanceOverlayState.Hidden);
        Get(FrameworkSetting.ExecutionMode, ExecutionMode.MultiThread);
        Get(FrameworkSetting.MasterVolume, 1.0);
        Get(FrameworkSetting.TrackVolume, 1.0);
        Get(FrameworkSetting.SampleVolume, 1.0);
        Get(FrameworkSetting.AudioBackend, AudioBackend.Automatic);
        Get(FrameworkSetting.AudioDeviceBufferFrames, SDLAudioManager.DEFAULT_DEVICE_BUFFER_FRAMES);
        Get(FrameworkSetting.WindowMode, WindowMode.Windowed);
        Get(FrameworkSetting.HardwareAcceleration, true);
        Get(FrameworkSetting.VideoZeroCopy, true);
        Get(FrameworkSetting.RendererType, RendererType.Automatic);
        Get(FrameworkSetting.WindowX, -1);
        Get(FrameworkSetting.WindowY, -1);
        Get(FrameworkSetting.WindowWidth, -1);
        Get(FrameworkSetting.WindowHeight, -1);
        Get(FrameworkSetting.RelativeMouseMode, false);
        Get(FrameworkSetting.CursorSensitivity, 1.0);
    }
}
