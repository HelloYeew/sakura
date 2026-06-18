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

    public Texture WhitePixel { get; }

    public MetalTextureManager(IRenderer renderer, Storage storage, IImageLoader imageLoader)
    {
        this.renderer = renderer;
        this.storage = storage;
        this.imageLoader = imageLoader;

        // The Metal renderer owns a 1x1 white texture; reuse it so solid-colour drawables sample white.
        WhitePixel = renderer.WhitePixel;
        missingTexture = new Texture(renderer.CreateNativeTexture(1, 1));
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
            byte[] pixelDataCopy = rawImage.Data.ToArray();

            var nativeTexture = renderer.CreateNativeTexture(rawImage.Width, rawImage.Height);
            var texture = new Texture(nativeTexture);

            rawImage.Dispose();

            renderer.ScheduleToDrawThread(() => nativeTexture.Upload(pixelDataCopy));

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

        byte[] dataCopy = pixelData.ToArray();

        renderer.ScheduleToDrawThread(() =>
        {
            ReadOnlySpan<byte> span = dataCopy;
            nativeTexture.Upload(span);
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

    public bool Evict(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        if (textureCache.TryGetValue(path, out var texture))
        {
            if (texture.BackendTexture != null && texture.BackendTexture != WhitePixel.BackendTexture && texture.BackendTexture != missingTexture.BackendTexture)
                renderer.ScheduleToDrawThread(() => texture.BackendTexture!.Dispose());

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
            if (texture.BackendTexture != null && texture.BackendTexture != WhitePixel.BackendTexture && texture.BackendTexture != missingTexture.BackendTexture)
                renderer.ScheduleToDrawThread(() => texture.BackendTexture!.Dispose());
        }

        textureCache.Clear();
    }

    public IEnumerable<Texture> GetAllTextures() => textureCache.Values;

    public void RegisterVideoTexture(IVideoTexture texture) => videoTextures.TryAdd(texture, 0);
    public void UnregisterVideoTexture(IVideoTexture texture) => videoTextures.TryRemove(texture, out _);
    public IEnumerable<IVideoTexture> GetAllVideoTextures() => videoTextures.Keys;
}
