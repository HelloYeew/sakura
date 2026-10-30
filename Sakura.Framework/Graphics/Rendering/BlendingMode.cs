// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering;

public enum BlendingMode
{
    /// <summary>
    /// Standard transparency.
    /// Math: Source * SrcAlpha + Destination * (1 - SrcAlpha)
    /// </summary>
    Alpha,

    /// <summary>
    /// Adds color values together. Great for glowing effects, HUDs, and light overlays.
    /// Math: Source * SrcAlpha + Destination * 1; alpha: Destination alpha, unchanged
    /// </summary>
    /// <remarks>
    /// Light covers nothing, so it leaves the target's alpha as it was. On screen alpha isn't seen; in an
    /// offscreen buffer (a <c>BufferedContainer</c>) it's the coverage the composite reads back, and light that
    /// added to it would come back as an opaque patch over whatever is behind the container.
    /// </remarks>
    Additive,

    /// <summary>
    /// Ignores transparency and overwrites the destination pixels.
    /// Math: Source * 1 + Destination * 0
    /// </summary>
    Opaque,

    /// <summary>
    /// Multiplies the destination color by the source color. Great for shadows or darkening.
    /// Math: Source * DstColor + Destination * (1 - SrcAlpha)
    /// </summary>
    Multiply,

    /// <summary>
    /// Inverts both colors, multiplies them, and inverts the result. Great for lightening.
    /// Math: Source * 1 + Destination * (1 - SrcColor)
    /// </summary>
    Screen,

    /// <summary>
    /// Used when texture RGB values are already multiplied by their Alpha.
    /// Math: Source * 1 + Destination * (1 - SrcAlpha)
    /// </summary>
    Premultiplied
}
