// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
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
    private readonly List<AtlasPage> pages = new List<AtlasPage>();

    private const int padding = 1;

    public Texture Texture { get; }

    public TextureAtlas(GL gl, int width, int height)
    {
        this.gl = gl;
        this.width = width;
        this.height = height;

        pages.Add(new AtlasPage(gl, width, height));
    }

    /// <summary>
    /// Add a bitmap to the atlas and return the texture region.
    /// </summary>
    public Texture? AddRegion(int regionWidth, int regionHeight, ReadOnlySpan<byte> rgbaData)
    {
        // Try to add to existing pages (usually the last one is enough, but we check specifically the last active one)
        var page = pages[^1]; // Get last page

        if (!canFitInPage(page, regionWidth, regionHeight))
        {
            // 2. If it doesn't fit, create a new page
            page = new AtlasPage(gl, width, height);
            pages.Add(page);
        }

        // if it still doesn't fit (glyph larger than entire texture?), fail.
        if (!canFitInPage(page, regionWidth, regionHeight))
            return null;

        // Move cursor for new row if needed
        if (page.CurrentX + regionWidth + padding > width)
        {
            page.CurrentX = 0;
            page.CurrentY += page.RowHeight + padding;
            page.RowHeight = 0;
        }

        // Upload data
        page.GlTexture.Bind();
        gl.TexSubImage2D(TextureTarget.Texture2D, 0, page.CurrentX, page.CurrentY, (uint)regionWidth, (uint)regionHeight, PixelFormat.Rgba, PixelType.UnsignedByte, rgbaData);

        // Calculate UVs
        float u = (float)page.CurrentX / width;
        float v = (float)page.CurrentY / height;
        float uw = (float)regionWidth / width;
        float vh = (float)regionHeight / height;

        // Create the texture wrapper pointing to THIS specific page's GL ID
        var region = new Texture(page.GlTexture, new Maths.RectangleF(u, v, uw, vh));

        // Advance cursor
        page.CurrentX += regionWidth + padding;
        page.RowHeight = Math.Max(page.RowHeight, regionHeight);

        return region;
    }

    /// <summary>
    /// Check if a region can fit in the current page
    /// </summary>
    /// <param name="page">The <see cref="AtlasPage"/> to test against</param>
    /// <param name="areaWidth">Width of the region to fit</param>
    /// <param name="areaHeight">Height of the region to fit</param>
    /// <returns>True if it can fit, false otherwise</returns>
    private bool canFitInPage(AtlasPage page, int areaWidth, int areaHeight)
    {
        // Simulate where it would go
        int testX = page.CurrentX;
        int testY = page.CurrentY;

        // Does it fit on current line?
        if (testX + areaWidth + padding > width)
        {
            // No, move to next line
            testX = 0;
            testY += page.RowHeight + padding;
        }

        // Does it fit vertically?
        return (testY + areaHeight + padding <= height);
    }

    public void Dispose()
    {
        glTexture.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A single page of the texture atlas
    /// </summary>
    private class AtlasPage : IDisposable
    {
        public GLTexture GlTexture { get; }
        public int CurrentX { get; set; } = 0;
        public int CurrentY { get; set; } = 0;
        public int RowHeight { get; set; } = 0;

        public AtlasPage(GL gl, int width, int height)
        {
            byte[] emptyData = new byte[width * height * 4];
            GlTexture = new GLTexture(gl, width, height, emptyData);
        }

        public void Dispose()
        {
            GlTexture.Dispose();
        }
    }
}
