// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sakura.Framework.Graphics.Textures;

public class HeadlessTextureManager : ITextureManager
{
    public Texture WhitePixel { get; }

    public TextureAtlas? Atlas => null;

    private readonly SharedTextureStore sharedTextures = new SharedTextureStore();

    public HeadlessTextureManager()
    {
        WhitePixel = createDummyTexture(1, 1);
    }

    public Texture Get(string path) => WhitePixel;

    public Texture FromPixelData(int width, int height, ReadOnlySpan<byte> pixelData, string cacheKey = null) => createDummyTexture(width, height);

    public bool TryAcquireSharedTexture(string cacheKey, out Texture texture) => sharedTextures.TryAcquire(cacheKey, out texture);

    public Texture AcquireSharedTexture(string cacheKey, int width, int height, ReadOnlySpan<byte> pixelData)
        => sharedTextures.AddOrAcquire(cacheKey, () => createDummyTexture(width, height));

    public void ReleaseSharedTexture(string cacheKey) => sharedTextures.Release(cacheKey, texture => texture.Dispose());

    public bool Evict(string path) => true;

    public void Dispose()
    {

    }

    private static Texture createDummyTexture(int width, int height) => new Texture(new HeadlessNativeTexture(width, height));

    public IEnumerable<Texture> GetAllTextures() => TextureRegistry.GetAll().Where(t => t.BackendTexture != null);

    public IEnumerable<Texture> GetCachedTextures() => new[] { WhitePixel };
    public void RegisterVideoTexture(IVideoTexture texture) { }
    public void UnregisterVideoTexture(IVideoTexture texture) { }
    public IEnumerable<IVideoTexture> GetAllVideoTextures() => Array.Empty<IVideoTexture>();
}
