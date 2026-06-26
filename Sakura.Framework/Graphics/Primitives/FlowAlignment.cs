// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Primitives;

/// <summary>
/// Defines how children are aligned along the flow axis within each line.
/// Has no visible effect on axes that are auto-sizing, since the container
/// then fits its content exactly and there is no free space to distribute.
/// </summary>
public enum FlowAlignment
{
    /// <summary>
    /// Children are packed at the start of the line (left for horizontal,
    /// top for vertical). This is the default and matches the original behavior.
    /// </summary>
    Start,

    /// <summary>
    /// Children are centered within the available space along the flow axis.
    /// </summary>
    Center,

    /// <summary>
    /// Children are packed at the end of the line (right for horizontal,
    /// bottom for vertical).
    /// </summary>
    End
}
