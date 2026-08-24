// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Configurations;

/// <summary>
/// Every setting the framework persists to <c>framework.ini</c>.
/// </summary>
[SettingSource("framework.ini")]
public enum FrameworkSetting
{
    FrameLimiter,
    ShowFpsGraph,
    MasterVolume,
    TrackVolume,
    SampleVolume,
    AudioBackend,
    WindowMode,
    ExecutionMode,
    HardwareAcceleration,
    RendererType,
    WindowX,
    WindowY,
    WindowWidth,
    WindowHeight,
    RelativeMouseMode,
    CursorSensitivity,
    AudioDeviceBufferFrames
}
