// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Text;

public class HeadlessFontStore : IFontStore
{
    private readonly Font dummyFont;
    private int cacheVersion = 0;

    public int CacheVersion => cacheVersion;

    public HeadlessFontStore(HeadlessTextureManager textureManager)
    {
        var atlasTexture = textureManager.WhitePixel;
        byte[] dummyData = new byte[0];
    }

    public void LoadDefaultFont(Storage resourceStorage)
    {

    }

    public void AddFont(Storage storage, string filename, string alias = null)
    {

    }

    public void AddFontFamily(Storage storage, string family, bool hasItalics = false, FontScript? script = null)
    {

    }

    public void AddFallbackFamily(string familyName)
    {

    }

    public void AddFallbackFamily(string familyName, FontScript script)
    {

    }

    public void SetScriptFamily(FontScript script, string familyName)
    {

    }

    public void InsertFallbackFamily(int index, string familyName)
    {

    }

    public void ClearFallbackFamilies()
    {

    }

    public FontScript HanScript { get; set; } = FontScript.ChineseSimplified;

    public IEnumerable<Font> GetFallbacks(FontUsage usage)
    {
        return Array.Empty<Font>();
    }

    public IEnumerable<Font> GetFallbacks(FontUsage usage, FontScript script)
    {
        return Array.Empty<Font>();
    }

    public IFontFallbackSource GetFallbackSource(FontUsage usage) => EmptyFontFallbackSource.INSTANCE;

    /// <summary>
    /// Stands in for a real chain when there are no fonts to fall back to, so consumers do not have to
    /// null-check the source.
    /// </summary>
    private sealed class EmptyFontFallbackSource : IFontFallbackSource
    {
        public static readonly EmptyFontFallbackSource INSTANCE = new EmptyFontFallbackSource();

        public IEnumerable<Font> GetFallbacks(FontScript script) => Array.Empty<Font>();
    }

    public Font Get(FontUsage usage)
    {
        return null;
    }

    public Font Get(string name)
    {
        return null;
    }

    public FontVariation GetVariation(FontUsage usage) => usage.ToVariation();

    public ShapedText Shape(FontUsage usage, string text, float dpiScale) => ShapedText.Empty;

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public void ClearCaches()
    {
        cacheVersion++;
    }

    public TextureAtlas Atlas => null;
}
