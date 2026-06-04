// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Graphics.Textures;

public class HeadlessTextureManager : ITextureManager
{
    public Texture WhitePixel { get; }

    public HeadlessTextureManager()
    {
        WhitePixel = createDummyTexture(1, 1);
    }

    public Texture Get(string path) => WhitePixel;

    public Texture FromPixelData(int width, int height, ReadOnlySpan<byte> pixelData, string cacheKey = null) => createDummyTexture(width, height);

    public bool Evict(string path) => true;

    public void Dispose()
    {

    }

    private static Texture createDummyTexture(int width, int height) => new Texture(new HeadlessNativeTexture(width, height));

    public IEnumerable<Texture> GetAllTextures() => new[] { WhitePixel };
    public void RegisterVideoTexture(IVideoTexture texture) { }
    public void UnregisterVideoTexture(IVideoTexture texture) { }
    public IEnumerable<IVideoTexture> GetAllVideoTextures() => Array.Empty<IVideoTexture>();
}
