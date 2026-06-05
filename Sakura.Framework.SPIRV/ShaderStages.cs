// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.SPIRV
{
    /// <summary>
    /// Identifies the stage of a shader program.
    /// </summary>
    [Flags]
    public enum ShaderStages : byte
    {
        None                 = 0,
        Vertex               = 1 << 0,
        Geometry             = 1 << 1,
        TessellationControl  = 1 << 2,
        TessellationEvaluation = 1 << 3,
        Fragment             = 1 << 4,
        Compute              = 1 << 5,
    }
}
