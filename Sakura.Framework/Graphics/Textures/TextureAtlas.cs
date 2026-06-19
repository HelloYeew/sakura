// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Manage a dynamic texture atlas
/// </summary>
public class TextureAtlas : IDisposable
{
    /// <summary>
    /// The default atlas page size
    /// </summary>
    public const int DEFAULT_SIZE = 1024;

    /// <summary>
    /// Textures with both dimensions at or below this size are eligible for atlas packing.
    /// Larger textures are uploaded as standalone textures to avoid wasting atlas space.
    /// </summary>
    public const int MAX_ATLAS_TEXTURE_SIZE = 256;

    private readonly IRenderer renderer;
    private readonly int width;
    private readonly int height;

    /// <summary>
    /// What this atlas is used for. Determines the statistics group its counters report under.
    /// </summary>
    private readonly AtlasUsage usage;

    /// <summary>
    /// The statistics group these atlas counters are reported under (e.g. "Fonts" or "Textures").
    /// </summary>
    private string statisticsGroup => usage.ToString();

    private readonly List<AtlasPage> pages = new List<AtlasPage>();

    private const int padding = 1;

    /// <summary>
    /// The width of each atlas page in pixels.
    /// </summary>
    public int PageWidth => width;

    /// <summary>
    /// The height of each atlas page in pixels.
    /// </summary>
    public int PageHeight => height;

    /// <summary>
    /// The number of atlas pages currently allocated.
    /// </summary>
    public int PageCount => pages.Count;

    public TextureAtlas(IRenderer renderer, int width = DEFAULT_SIZE, int height = DEFAULT_SIZE, AtlasUsage usage = AtlasUsage.Fonts)
    {
        this.renderer = renderer;
        this.width = width;
        this.height = height;
        this.usage = usage;

        pages.Add(new AtlasPage(renderer, width, height));
    }

    /// <summary>
    /// Whether a region of the given size could ever fit inside an empty atlas page.
    /// Regions larger than this should be uploaded as standalone textures instead.
    /// </summary>
    public bool CanFit(int regionWidth, int regionHeight) => regionWidth + padding * 2 <= width && regionHeight + padding * 2 <= height;

    /// <summary>
    /// Add a bitmap to the atlas and return the texture region.
    /// Returns <c>null</c> if the region is too large to ever fit on a page, callers should
    /// then fall back to creating a standalone texture.
    /// </summary>
    public Texture? AddRegion(int regionWidth, int regionHeight, ReadOnlySpan<byte> rgbaData)
    {
        // A region that cannot fit on an empty page must be handled as a standalone texture.
        if (!CanFit(regionWidth, regionHeight))
            return null;

        var page = pages[^1];

        if (!canFitInPage(page, regionWidth, regionHeight))
        {
            page = new AtlasPage(renderer, width, height);
            pages.Add(page);
            GlobalStatistics.Get<int>(statisticsGroup, "Atlas Pages").Value = pages.Count;
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
        INativeTexture targetNativeTexture = page.NativeTexture;

        renderer.ScheduleToDrawThread(() =>
        {
            ReadOnlySpan<byte> span = pixelDataCopy;
            targetNativeTexture.UploadRegion(destX, destY, regionWidth, regionHeight, span);
        });

        // Calculate UVs
        float u = (float)page.CurrentX / width;
        float v = (float)page.CurrentY / height;
        float uw = (float)regionWidth / width;
        float vh = (float)regionHeight / height;

        var region = new Texture(page.NativeTexture, new Maths.RectangleF(u, v, uw, vh));

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

        return testY + areaHeight + padding <= height;
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

        GlobalStatistics.Get<int>(statisticsGroup, "Atlas Pages").Value = pages.Count;
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
        return pages.Select(page => new Texture(page.NativeTexture));
    }

    /// <summary>
    /// Whether the given native texture is one of this atlas's pages. Used by texture managers
    /// to avoid disposing shared atlas pages when evicting an individual atlas-backed texture.
    /// </summary>
    public bool OwnsNativeTexture(INativeTexture? nativeTexture)
    {
        if (nativeTexture == null)
            return false;

        foreach (var page in pages)
        {
            if (page.NativeTexture == nativeTexture)
                return true;
        }

        return false;
    }

    private class AtlasPage : IDisposable
    {
        public INativeTexture NativeTexture { get; }
        public int CurrentX { get; set; } = 0;
        public int CurrentY { get; set; } = 0;
        public int RowHeight { get; set; } = 0;

        public AtlasPage(IRenderer renderer, int width, int height)
        {
            var nativeTexture = renderer.CreateNativeTexture(width, height);
            NativeTexture = nativeTexture;

            byte[] emptyData = new byte[width * height * 4];
            renderer.ScheduleToDrawThread(() => nativeTexture.Upload(emptyData));
        }

        public void Dispose()
        {
            NativeTexture.Dispose();
        }
    }
}
