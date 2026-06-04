// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Platform;

public interface IGraphicsSurface
{
    /// <summary>
    /// Get the address of the graphic API function from the window API.
    /// </summary>
    Func<string,nint> GetFunctionAddress { get; set; }

    /// <summary>
    /// Makes the graphics context current on the calling thread.
    /// </summary>
    Action MakeCurrent { get; set; }

    /// <summary>
    /// Clears the graphics context from the calling thread, allowing another thread to claim it.
    /// </summary>
    Action ClearCurrent { get; set; }
}
