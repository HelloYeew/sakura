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

    public void AddFallbackFamily(string familyName)
    {

    }

    public void InsertFallbackFamily(int index, string familyName)
    {

    }

    public void ClearFallbackFamilies()
    {

    }

    public IEnumerable<Font> GetFallbacks(FontUsage usage)
    {
        return Array.Empty<Font>();
    }

    public Font Get(FontUsage usage)
    {
        return null;
    }

    public Font Get(string name)
    {
        return null;
    }

    public void Dispose()
    {
        throw new System.NotImplementedException();
    }

    public TextureAtlas Atlas => null;
}
