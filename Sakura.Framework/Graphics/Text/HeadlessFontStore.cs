// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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
}
