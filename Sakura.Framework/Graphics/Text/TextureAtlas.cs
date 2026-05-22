// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Manage a dynamic texture atlas for storing rasterized glyphs.
/// </summary>
public class TextureAtlas : IDisposable
{
    private readonly IRenderer renderer;
    private readonly GL gl;
    private readonly int width;
    private readonly int height;

    private readonly List<AtlasPage> pages = new List<AtlasPage>();

    private const int padding = 1;

    public TextureAtlas(IRenderer renderer, GL gl, int width, int height)
    {
        this.renderer = renderer;
        this.gl = gl;
        this.width = width;
        this.height = height;

        pages.Add(new AtlasPage(renderer, gl, width, height));
    }

    /// <summary>
    /// Add a bitmap to the atlas and return the texture region.
    /// </summary>
    public Texture? AddRegion(int regionWidth, int regionHeight, ReadOnlySpan<byte> rgbaData)
    {
        var page = pages[^1]; // Get last page

        if (!canFitInPage(page, regionWidth, regionHeight))
        {
            page = new AtlasPage(renderer, gl, width, height);
            pages.Add(page);
            GlobalStatistics.Get<int>("Fonts", "Atlas Pages").Value = pages.Count;
        }

        if (!canFitInPage(page, regionWidth, regionHeight))
            return null;

        // Move cursor for new row if needed
        if (page.CurrentX + regionWidth + padding > width)
        {
            page.CurrentX = 0;
            page.CurrentY += page.RowHeight + padding;
            page.RowHeight = 0;
        }

        byte[] pixelDataCopy = rgbaData.ToArray();

        int destX = page.CurrentX;
        int destY = page.CurrentY;
        var targetGlTexture = page.GlTexture;

        renderer.ScheduleToDrawThread(() =>
        {
            targetGlTexture.Bind();
            ReadOnlySpan<byte> span = pixelDataCopy;
            gl.TexSubImage2D(TextureTarget.Texture2D, 0, destX, destY, (uint)regionWidth, (uint)regionHeight, PixelFormat.Rgba, PixelType.UnsignedByte, span);
            gl.GenerateMipmap(TextureTarget.Texture2D);
        });

        // Calculate UVs
        float u = (float)page.CurrentX / width;
        float v = (float)page.CurrentY / height;
        float uw = (float)regionWidth / width;
        float vh = (float)regionHeight / height;

        var region = new Texture(page.GlTexture, new Maths.RectangleF(u, v, uw, vh));

        // Advance cursor
        page.CurrentX += regionWidth + padding;
        page.RowHeight = Math.Max(page.RowHeight, regionHeight);

        return region;
    }

    private bool canFitInPage(AtlasPage page, int areaWidth, int areaHeight)
    {
        int testX = page.CurrentX;
        int testY = page.CurrentY;

        if (testX + areaWidth + padding > width)
        {
            testX = 0;
            testY += page.RowHeight + padding;
        }

        return (testY + areaHeight + padding <= height);
    }

    public void Clear()
    {
        for (int i = 1; i < pages.Count; i++)
        {
            pages[i].Dispose();
        }

        if (pages.Count > 1)
        {
            pages.RemoveRange(1, pages.Count - 1);
        }

        pages[0].CurrentX = 0;
        pages[0].CurrentY = 0;
        pages[0].RowHeight = 0;

        GlobalStatistics.Get<int>("Fonts", "Atlas Pages").Value = pages.Count;
    }

    public void Dispose()
    {
        foreach (var page in pages)
        {
            page.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    public IEnumerable<Texture> GetAllPages()
    {
        foreach (var page in pages)
        {
            yield return new Texture(page.GlTexture);
        }
    }

    private class AtlasPage : IDisposable
    {
        public GLTexture GlTexture { get; }
        public int CurrentX { get; set; } = 0;
        public int CurrentY { get; set; } = 0;
        public int RowHeight { get; set; } = 0;

        public AtlasPage(IRenderer renderer, GL gl, int width, int height)
        {
            GlTexture = new GLTexture(gl, width, height);

            // Queue the initial blank texture allocation to the Draw thread
            byte[] emptyData = new byte[width * height * 4];
            renderer.ScheduleToDrawThread(() =>
            {
                GlTexture.Upload(emptyData);
            });
        }

        public void Dispose()
        {
            GlTexture.Dispose();
        }
    }
}
