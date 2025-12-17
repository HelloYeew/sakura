// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Defines the public contract for a texture management service.
/// </summary>
public interface ITextureManager : IDisposable
{
    /// <summary>
    /// A 1x1 white pixel texture.
    /// </summary>
    Texture WhitePixel { get; }

    /// <summary>
    /// Retrieves a texture from the specified path.
    /// Loads it from storage if not already cached.
    /// </summary>
    /// <param name="path">The path to the texture in storage.</param>
    /// <returns>A <see cref="Texture"/> object. Returns <see cref="WhitePixel"/> if the path is null or empty, or a fallback texture on load failure.</returns>
    Texture Get(string path);

    /// <summary>
    /// Creates a texture from raw pixel data.
    /// </summary>
    /// <param name="width">Width of the texture in pixels.</param>
    /// <param name="height">Height of the texture in pixels.</param>
    /// <param name="pixelData">Raw pixel data in RGBA format.</param>
    /// <returns>A new <see cref="Texture"/> object.</returns>
    Texture FromPixelData(int width, int height, ReadOnlySpan<byte> pixelData);
}
