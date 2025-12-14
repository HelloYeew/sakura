// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Textures;
using Silk.NET.OpenGL;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Manage a dynamic texture atlas for storing rasterized glyphs.
/// </summary>
public class TextureAtlas : IDisposable
{
    private readonly GL gl;
    private readonly int width;
    private readonly int height;
    private readonly GLTexture glTexture;

    // TODO: Just basic implementation, maybe change to bin-packing later
    // TODO: Add support for multiple atlas pages
    private int currentX = 0;
    private int currentY = 0;
    private int currentRowHeight = 0;

    private const int padding = 1;

    public Texture Texture { get; }

    public TextureAtlas(GL gl, int width, int height)
    {
        this.gl = gl;
        this.width = width;
        this.height = height;

        byte[] emptyData = new byte[width * height * 4];
        glTexture = new GLTexture(gl, width, height, emptyData);
        Texture = new Texture(glTexture);
    }

    /// <summary>
    /// Add a bitmap to the atlas and return the texture region.
    /// </summary>
    public Texture? AddRegion(int width, int height, ReadOnlySpan<byte> rgbaData)
    {
        // Move to next row if this glyph doesn't fit horizontally
        if (currentX + width + padding > this.width)
        {
            currentX = 0;
            currentY += currentRowHeight + padding;
            currentRowHeight = 0;
        }

        // Check if we ran out of space vertically
        if (currentY + height + padding > this.height)
        {
            // Atlas is full. In a real engine, you'd create a second atlas page here.
            return null;
        }

        // Upload data to the specific region of the texture
        glTexture.Bind();
        gl.TexSubImage2D(TextureTarget.Texture2D, 0, currentX, currentY, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, rgbaData);

        // Calculate UVs
        float u = (float)currentX / this.width;
        float v = (float)currentY / this.height;
        float uw = (float)width / this.width;
        float vh = (float)height / this.height;

        var region = new Texture(glTexture, new Maths.RectangleF(u, v, uw, vh));

        // Advance cursor
        currentX += width + padding;
        currentRowHeight = Math.Max(currentRowHeight, height);

        return region;
    }

    public void Dispose()
    {
        glTexture.Dispose();
        GC.SuppressFinalize(this);
    }
}
