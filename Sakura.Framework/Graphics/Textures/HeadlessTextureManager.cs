// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Textures;

public class HeadlessTextureManager : ITextureManager
{
    public Texture WhitePixel { get; }

    public TextureAtlas? Atlas => null;

    private readonly SharedTextureStore sharedTextures = new SharedTextureStore();
    private readonly Storage? storage;

    /// <summary>
    /// Creates a texture manager that decodes nothing and hands out placeholder textures.
    /// </summary>
    /// <param name="storage">Where <see cref="TryGet(string, out Texture?)"/> looks for a file. Without one, no file is found.</param>
    public HeadlessTextureManager(Storage? storage = null)
    {
        this.storage = storage;
        WhitePixel = new Texture(new HeadlessNativeTexture(1, 1), TextureOwnership.Shared);
    }

    public Texture Get(string path) => WhitePixel;

    public Texture Get(string path, ImageLoadOptions decode) => WhitePixel;

    public bool TryGet(string path, [NotNullWhen(true)] out Texture? texture) => TryGet(path, ImageLoadOptions.FullSize, out texture);

    public bool TryGet(string path, ImageLoadOptions decode, [NotNullWhen(true)] out Texture? texture)
    {
        // Nothing is decoded without a GPU, so a file that is there answers with the white pixel, as Get does.
        texture = !string.IsNullOrEmpty(path) && storage?.Exists(path) == true ? WhitePixel : null;
        return texture != null;
    }

    public Texture FromPixelData(int width, int height, ReadOnlySpan<byte> pixelData, string cacheKey = null) => createDummyTexture(width, height);

    public Texture? CreateFromStream(Stream stream, TextureCreationOptions options)
    {
        // there is no GPU to upload to but honors the share key, so a headless run
        // exercises the same acquire/release balance as a real one.
        string? shareKey = options.ShareKey;

        if (!string.IsNullOrEmpty(shareKey))
        {
            var shared = sharedTextures.AddOrAcquire(shareKey, () => new Texture(new HeadlessNativeTexture(1, 1)) { Name = options.Name });
            SharedTextureStatistics.SetKeyCount(sharedTextures.Count);
            return shared;
        }

        return new Texture(new HeadlessNativeTexture(1, 1)) { Name = options.Name };
    }

    public bool TryAcquireSharedTexture(string cacheKey, out Texture texture) => sharedTextures.TryAcquire(cacheKey, out texture);

    public Texture AcquireSharedTexture(string cacheKey, int width, int height, ReadOnlySpan<byte> pixelData)
        => sharedTextures.AddOrAcquire(cacheKey, () => createDummyTexture(width, height));

    public void ReleaseSharedTexture(string cacheKey)
    {
        sharedTextures.Release(cacheKey, texture => texture.Dispose());
        SharedTextureStatistics.SetKeyCount(sharedTextures.Count);
    }

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
