// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

/// <summary>
/// Whether each frame waits, before it takes the update to draw, until it can start just in time for the GPU, so no
/// finished frame sits in a queue behind the GPU and input reaches the screen sooner. It matters most when the GPU can't
/// keep up with the frame limiter. The renderer carries it with NVIDIA Reflex or AMD Anti-Lag 2 where the graphics card
/// and driver have them, and otherwise waits for the GPU to finish the frame before (see
/// <see cref="Graphics.Rendering.LowLatencyProvider"/>). Direct3D 11 and Vulkan only.
/// </summary>
public enum LowLatencyMode
{
    /// <summary>
    /// Frames start as soon as the frame limiter and the display let them, and no driver latency feature is touched.
    /// </summary>
    Off,

    /// <summary>
    /// Each frame starts just in time for the GPU.
    /// </summary>
    On,

    /// <summary>
    /// As <see cref="On"/>, and NVIDIA Reflex also keeps the GPU at its top clock while it's idle most of the frame,
    /// which a light game is: lower latency when the CPU is the limit, for more power. The same as <see cref="On"/>
    /// everywhere else.
    /// </summary>
    Boost
}
