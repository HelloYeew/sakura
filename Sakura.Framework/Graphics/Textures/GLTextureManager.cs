// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Textures;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLTextureManager : ITextureManager
{
    private readonly GL gl;
    private readonly Storage storage;
    private readonly IImageLoader imageLoader;
    private readonly Dictionary<string, Texture> textureCache = new Dictionary<string, Texture>();

    private readonly Texture missingTexture;

    /// <summary>
    /// A 1x1 white pixel texture.
    /// </summary>
    public Texture WhitePixel { get; }

    public GLTextureManager(GL gl, Storage storage, IImageLoader imageLoader)
    {
        this.gl = gl;
        this.storage = storage;
        this.imageLoader = imageLoader;
        WhitePixel = new Texture(GLTexture.WhitePixel);
        missingTexture = createNullTexture();
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

            using var rawImage = imageLoader.Load(stream);
            var glTexture = new GLTexture(gl, rawImage.Width, rawImage.Height, rawImage.Data);
            var texture = new Texture(glTexture);

            textureCache[path] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load texture '{path}': {ex.Message}");
            return missingTexture;
        }
    }

    /// <summary>
    /// Create a simple
    /// </summary>
    /// <returns></returns>
    private Texture createNullTexture()
    {
        const int width = 1;
        const int height = 1;
        byte[] data = new byte[width * height * 4];

        var glTex = new GLTexture(gl, width, height, data);
        return new Texture(glTex);
    }

    public void Dispose()
    {
        foreach (var tex in textureCache.Values)
        {
            if (tex != WhitePixel && tex.GlTexture.Handle != missingTexture.GlTexture.Handle)
            {
                tex.GlTexture.Dispose();
            }
        }

        textureCache.Clear();
    }
}
