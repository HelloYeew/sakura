// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLTextureManager : ITextureManager
{
    private readonly GL gl;
    private readonly Storage storage;
    private readonly IImageLoader imageLoader;
    private readonly IRenderer renderer;
    private readonly Dictionary<string, Texture> textureCache = new Dictionary<string, Texture>();
    private readonly ConcurrentDictionary<IVideoTexture, byte> videoTextures = new ConcurrentDictionary<IVideoTexture, byte>();

    private readonly Texture missingTexture;
    private readonly SharedTextureStore sharedTextures = new SharedTextureStore();

    /// <summary>
    /// A 1x1 white pixel texture.
    /// </summary>
    public Texture WhitePixel { get; }

    public TextureAtlas Atlas { get; }

    public GLTextureManager(IRenderer renderer, GL gl, Storage storage, IImageLoader imageLoader)
    {
        this.renderer = renderer;
        this.gl = gl;
        this.storage = storage;
        this.imageLoader = imageLoader;
        // Both are handed to many callers and live for the process, so neither may be released by one of
        // them (see TextureOwnership.Shared)
        WhitePixel = new Texture(GLTexture.WhitePixel, TextureOwnership.Shared);
        missingTexture = createNullTexture();
        Atlas = new TextureAtlas(renderer, usage: AtlasUsage.Textures);
    }

    /// <summary>
    /// Retrieves a texture from the specified path.
    /// Loads it from storage if not already cached.
    /// </summary>
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
                texture = Atlas.AddRegion(imageWidth, imageHeight, rawImage.Data);

                // The atlas takes its own pooled copy, since the region is blotted into a page rather than
                // uploaded as a whole texture.
                rawImage.Dispose();
            }

            if (texture == null)
            {
                var glTexture = new GLTexture(gl, imageWidth, imageHeight);
                texture = new Texture(glTexture);

                // Ownership of the decoded pixels passes to the upload, which releases them afterward, so
                // nothing is copied between decode and GPU.
                TextureUploads.ScheduleOwned(renderer, glTexture, rawImage);
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
        var glTexture = new GLTexture(gl, width, height);
        var texture = new Texture(glTexture);

        TextureUploads.Schedule(renderer, glTexture, width, height, pixelData);

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
        // Copied up front because a span cannot be captured by the creation callback, and released again
        // below if an existing entry turned out to satisfy the request.
        var pixels = ImageRawData.CopyFrom(width, height, pixelData);
        bool used = false;

        var texture = sharedTextures.AddOrAcquire(cacheKey, () =>
        {
            used = true;

            var glTexture = new GLTexture(gl, width, height);
            var created = new Texture(glTexture) { Name = cacheKey };
            TextureUploads.ScheduleOwned(renderer, glTexture, pixels);
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

    private Texture createNullTexture()
    {
        var glTexture = new GLTexture(gl, 1, 1);
        return new Texture(glTexture, TextureOwnership.Shared);
    }

    /// <summary>
    /// Releases a texture this manager handed out, on the draw thread. See
    /// <see cref="MetalTextureManager"/>'s equivalent for why this needs no per-call-site ownership
    /// checks.
    /// </summary>
    private void release(Texture texture)
    {
        if (texture == null)
            return;

        renderer.ScheduleToDrawThread(texture.Dispose);
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
        Atlas.Dispose();
    }

    /// <summary>
    /// Returns only standalone (non-atlas) cached textures, so the viewer can show atlas pages separately.
    /// </summary>
    public IEnumerable<Texture> GetAllTextures() => TextureRegistry.GetAll()
        .Where(t => t.BackendTexture != null && !Atlas.OwnsNativeTexture(t.BackendTexture));

    public IEnumerable<Texture> GetCachedTextures() => textureCache.Values.Where(t => !Atlas.OwnsNativeTexture(t.BackendTexture));

    public void RegisterVideoTexture(IVideoTexture texture) => videoTextures.TryAdd(texture, 0);
    public void UnregisterVideoTexture(IVideoTexture texture) => videoTextures.TryRemove(texture, out _);
    public IEnumerable<IVideoTexture> GetAllVideoTextures() => videoTextures.Keys;
}
