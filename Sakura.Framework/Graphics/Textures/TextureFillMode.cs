// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Textures;

public enum TextureFillMode
{
    /// <summary>
    /// The texture is stretched to fill the drawable's size (default).
    /// Aspect ratio is ignored.
    /// </summary>
    Stretch,

    /// <summary>
    /// The texture is scaled to fit within the drawable's bounds while maintaining aspect ratio.
    /// This may result in empty space (letterboxing) if aspect ratios differ.
    /// </summary>
    Fit,

    /// <summary>
    /// The texture is scaled to fill the entire drawable's bounds while maintaining aspect ratio.
    /// Parts of the texture may be cropped if aspect ratios differ.
    /// </summary>
    Fill,

    /// <summary>
    /// Repeats the texture to fill the DrawSize.
    /// </summary>
    Tile
}
