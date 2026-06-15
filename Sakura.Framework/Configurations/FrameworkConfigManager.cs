// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Platform;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Configurations;

public class FrameworkConfigManager : ConfigManager<FrameworkSetting>
{
    public FrameworkConfigManager(Storage storage) : base(storage)
    {
        initializeDefaults();
    }

    private void initializeDefaults()
    {
        Get(FrameworkSetting.FrameLimiter, FrameSync.Limit2x);
        Get(FrameworkSetting.ShowFpsGraph, PerformanceOverlayState.Hidden);
        Get(FrameworkSetting.ExecutionMode, ExecutionMode.MultiThread);
        Get(FrameworkSetting.MasterVolume, 1.0);
        Get(FrameworkSetting.TrackVolume, 1.0);
        Get(FrameworkSetting.SampleVolume, 1.0);
        Get(FrameworkSetting.WindowMode, WindowMode.Windowed);
        Get(FrameworkSetting.HardwareAcceleration, true);
        Get(FrameworkSetting.RendererType, RendererType.Automatic);
        Get(FrameworkSetting.WindowX, -1);
        Get(FrameworkSetting.WindowY, -1);
        Get(FrameworkSetting.WindowWidth, -1);
        Get(FrameworkSetting.WindowHeight, -1);
        Get(FrameworkSetting.RelativeMouseMode, false);
        Get(FrameworkSetting.CursorSensitivity, 1.0);
    }
}
