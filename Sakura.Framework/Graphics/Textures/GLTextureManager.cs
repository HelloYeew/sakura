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
        WhitePixel = new Texture(GLTexture.WhitePixel);
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

            // force a copy of the pooled memory immediately on the update thread
            byte[] pixelDataCopy = rawImage.Data.ToArray();
            int imageWidth = rawImage.Width;
            int imageHeight = rawImage.Height;

            // dispose the native image on the thread that created it
            rawImage.Dispose();

            Texture? texture = null;

            if (imageWidth <= TextureAtlas.MAX_ATLAS_TEXTURE_SIZE && imageHeight <= TextureAtlas.MAX_ATLAS_TEXTURE_SIZE)
                texture = Atlas.AddRegion(imageWidth, imageHeight, pixelDataCopy);

            if (texture == null)
            {
                var glTexture = new GLTexture(gl, imageWidth, imageHeight);
                texture = new Texture(glTexture);

                renderer.ScheduleToDrawThread(() =>
                {
                    glTexture.Upload(pixelDataCopy);
                });
            }

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

        byte[] dataCopy = pixelData.ToArray();

        renderer.ScheduleToDrawThread(() =>
        {
            ReadOnlySpan<byte> span = dataCopy;
            glTexture.Upload(span);
        });

        if (!string.IsNullOrEmpty(cacheKey))
        {
            if (textureCache.TryGetValue(cacheKey, out var oldTexture))
            {
                var oldNative = oldTexture.BackendTexture;
                renderer.ScheduleToDrawThread(() => oldNative?.Dispose());
            }

            textureCache[cacheKey] = texture;
            GlobalStatistics.Get<int>("Textures", "Loaded Textures").Value = textureCache.Count;
            GlobalStatistics.Get<int>("Textures", "Texture Updates").Value++;
        }

        return texture;
    }

    private Texture createNullTexture()
    {
        var glTexture = new GLTexture(gl, 1, 1);
        return new Texture(glTexture);
    }

    public bool Evict(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        if (textureCache.TryGetValue(path, out var texture))
        {
            if (texture.BackendTexture != null && texture.BackendTexture != WhitePixel.BackendTexture && texture.BackendTexture != missingTexture.BackendTexture && !Atlas.OwnsNativeTexture(texture.BackendTexture))
            {
                renderer.ScheduleToDrawThread(() =>
                {
                    texture.BackendTexture!.Dispose();
                });
            }

            textureCache.Remove(path);

            GlobalStatistics.Get<int>("Textures", "Loaded Textures").Value = textureCache.Count;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var texture in textureCache.Values)
        {
            if (texture.BackendTexture != null && texture.BackendTexture != WhitePixel.BackendTexture && texture.BackendTexture != missingTexture.BackendTexture && !Atlas.OwnsNativeTexture(texture.BackendTexture))
            {
                renderer.ScheduleToDrawThread(() =>
                {
                    texture.BackendTexture!.Dispose();
                });
            }
        }

        textureCache.Clear();
        Atlas.Dispose();
    }

    /// <summary>
    /// Returns only standalone (non-atlas) cached textures, so the viewer can show atlas pages separately.
    /// </summary>
    public IEnumerable<Texture> GetAllTextures() => textureCache.Values.Where(t => !Atlas.OwnsNativeTexture(t.BackendTexture));

    public void RegisterVideoTexture(IVideoTexture texture) => videoTextures.TryAdd(texture, 0);
    public void UnregisterVideoTexture(IVideoTexture texture) => videoTextures.TryRemove(texture, out _);
    public IEnumerable<IVideoTexture> GetAllVideoTextures() => videoTextures.Keys;
}
