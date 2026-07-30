// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Texture manager for the Metal backend. Mirrors <see cref="GLTextureManager"/> but creates
/// textures via <see cref="IRenderer.CreateNativeTexture"/> (returning <see cref="MetalTexture"/>),
/// so it carries no Metal-specific code beyond the backing renderer.
/// </summary>
public class MetalTextureManager : ITextureManager
{
    private readonly Storage storage;
    private readonly IImageLoader imageLoader;
    private readonly IRenderer renderer;
    private readonly Dictionary<string, Texture> textureCache = new Dictionary<string, Texture>();
    private readonly ConcurrentDictionary<IVideoTexture, byte> videoTextures = new ConcurrentDictionary<IVideoTexture, byte>();

    private readonly Texture missingTexture;
    private readonly TextureAtlas atlas;
    private readonly SharedTextureStore sharedTextures = new SharedTextureStore();

    public Texture WhitePixel { get; }

    public TextureAtlas? Atlas => atlas;

    public MetalTextureManager(IRenderer renderer, Storage storage, IImageLoader imageLoader)
    {
        this.renderer = renderer;
        this.storage = storage;
        this.imageLoader = imageLoader;

        // The Metal renderer owns a 1x1 white texture; reuse it so solid-color drawables sample white.
        WhitePixel = renderer.WhitePixel;

        // Handed back from Get() on any failure, so many callers end up holding it. Shared ownership is
        // what stops one of them disposing it out from under the others.
        missingTexture = new Texture(renderer.CreateNativeTexture(1, 1), TextureOwnership.Shared);
        atlas = new TextureAtlas(renderer, usage: AtlasUsage.Textures);
    }

    public Texture Get(string path)
    {
        if (string.IsNullOrEmpty(path))
            return WhitePixel;

        if (textureCache.TryGetValue(path, out var cachedTexture))
            return cachedTexture;

        try
        {
            using var stream = storage.GetStream(path);
            if (stream == null) throw new FileNotFoundException($"Texture not found: {path}");

            var rawImage = imageLoader.Load(stream);
            int imageWidth = rawImage.Width;
            int imageHeight = rawImage.Height;

            Texture? texture = null;

            if (imageWidth <= TextureAtlas.MAX_ATLAS_TEXTURE_SIZE && imageHeight <= TextureAtlas.MAX_ATLAS_TEXTURE_SIZE)
            {
                texture = atlas.AddRegion(imageWidth, imageHeight, rawImage.Data);

                // The atlas takes its own pooled copy, since the region is blitted into a page rather than
                // uploaded as a whole texture.
                rawImage.Dispose();
            }

            if (texture == null)
            {
                var nativeTexture = renderer.CreateNativeTexture(imageWidth, imageHeight);
                texture = new Texture(nativeTexture);

                // Ownership of the decoded pixels passes to the upload, which releases them afterwards, so
                // nothing is copied between decode and GPU.
                TextureUploads.ScheduleOwned(renderer, nativeTexture, rawImage);
            }

            texture.Name = path;
            textureCache[path] = texture;
            GlobalStatistics.Get<int>("Textures", "Loaded Textures").Value = textureCache.Count;
            return texture;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load texture '{path}': {ex.Message}");
            return missingTexture;
        }
    }

    public Texture FromPixelData(int width, int height, ReadOnlySpan<byte> pixelData, string cacheKey = null)
    {
        var nativeTexture = renderer.CreateNativeTexture(width, height);
        var texture = new Texture(nativeTexture);

        TextureUploads.Schedule(renderer, nativeTexture, width, height, pixelData);

        if (!string.IsNullOrEmpty(cacheKey))
        {
            if (textureCache.TryGetValue(cacheKey, out var oldTexture))
                release(oldTexture);

            texture.Name = cacheKey;
            textureCache[cacheKey] = texture;
            GlobalStatistics.Get<int>("Textures", "Loaded Textures").Value = textureCache.Count;
            GlobalStatistics.Get<int>("Textures", "Texture Updates").Value++;
        }

        return texture;
    }

    public Texture? CreateFromStream(Stream stream, TextureCreationOptions options)
        => TextureUploads.FromStream(stream, options, renderer, imageLoader, sharedTextures, release);

    public bool TryAcquireSharedTexture(string cacheKey, out Texture texture)
    {
        bool hit = sharedTextures.TryAcquire(cacheKey, out texture);

        if (hit)
            SharedTextureStatistics.RecordHit();

        return hit;
    }

    public Texture AcquireSharedTexture(string cacheKey, int width, int height, ReadOnlySpan<byte> pixelData)
    {
        // Copied up front because a span cannot be captured by the create callback, and released again
        // below if an existing entry turned out to satisfy the request.
        var pixels = ImageRawData.CopyFrom(width, height, pixelData);
        bool used = false;

        var texture = sharedTextures.AddOrAcquire(cacheKey, () =>
        {
            used = true;

            var nativeTexture = renderer.CreateNativeTexture(width, height);
            var created = new Texture(nativeTexture)
            {
                Name = cacheKey
            };
            TextureUploads.ScheduleOwned(renderer, nativeTexture, pixels);
            return created;
        });

        if (!used)
        {
            pixels.Dispose();
            SharedTextureStatistics.RecordHit();
        }

        SharedTextureStatistics.SetKeyCount(sharedTextures.Count);
        return texture;
    }

    public void ReleaseSharedTexture(string cacheKey)
    {
        sharedTextures.Release(cacheKey, release);
        SharedTextureStatistics.SetKeyCount(sharedTextures.Count);
    }

    public bool Evict(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        if (textureCache.TryGetValue(path, out var texture))
        {
            release(texture);

            textureCache.Remove(path);
            GlobalStatistics.Get<int>("Textures", "Loaded Textures").Value = textureCache.Count;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var texture in textureCache.Values)
            release(texture);

        textureCache.Clear();
        atlas.Dispose();
    }

    /// <summary>
    /// Releases a texture this manager handed out, on the draw thread.
    /// </summary>
    /// <remarks>
    /// <see cref="Texture.Dispose"/> is the whole release: it unregisters the texture and destroys its
    /// GPU resource only if it owns one. That is why there is no longer a per-call-site check for the
    /// shared white/missing singletons or for atlas-owned pages — <see cref="TextureOwnership"/> carries
    /// it. Scheduled rather than immediate because destroying a GPU resource must happen on the draw
    /// thread.
    /// </remarks>
    private void release(Texture texture)
    {
        if (texture == null)
            return;

        renderer.ScheduleToDrawThread(texture.Dispose);
    }

    public IEnumerable<Texture> GetAllTextures() => TextureRegistry.GetAll()
        .Where(t => t.BackendTexture != null && !atlas.OwnsNativeTexture(t.BackendTexture));

    public IEnumerable<Texture> GetCachedTextures() => textureCache.Values.Where(t => !atlas.OwnsNativeTexture(t.BackendTexture));

    public void RegisterVideoTexture(IVideoTexture texture) => videoTextures.TryAdd(texture, 0);
    public void UnregisterVideoTexture(IVideoTexture texture) => videoTextures.TryRemove(texture, out _);
    public IEnumerable<IVideoTexture> GetAllVideoTextures() => videoTextures.Keys;
}
