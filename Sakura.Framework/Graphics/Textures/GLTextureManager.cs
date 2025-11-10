// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Graphics.Textures;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLTextureManager : ITextureManager
{
    private readonly GL gl;
    private readonly Storage storage;
    private readonly Dictionary<string, Texture> textureCache = new Dictionary<string, Texture>();

    private readonly Texture missingTexture;

    /// <summary>
    /// A 1x1 white pixel texture.
    /// </summary>
    public Texture WhitePixel { get; }

    public GLTextureManager(GL gl, Storage storage)
    {
        this.gl = gl;
        this.storage = storage;
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
            using (var stream = storage.GetStream(path))
            {
                if (stream == null)
                    throw new FileNotFoundException($"Image file not found at storage path: {path}");

                using (var image = Image.Load<Rgba32>(stream))
                {
                    // Get pixel data.
                    // We need a contiguous block of memory to upload to OpenGL.
                    // GetPixelMemoryGroup() returns one or more memory blocks.
                    var memoryGroup = image.GetPixelMemoryGroup();

                    GLTexture glTexture;

                    if (memoryGroup.Count == 1)
                    {
                        // The image is contiguous in memory. We can use its span directly.
                        var pixelDataSpan = MemoryMarshal.AsBytes(memoryGroup[0].Span);
                        glTexture = new GLTexture(gl, image.Width, image.Height, pixelDataSpan);
                    }
                    else
                    {
                        // The image is not contiguous. We must copy it to a new contiguous array.
                        // This is less efficient but necessary for large images.
                        byte[] pixelDataArray = new byte[image.Width * image.Height * 4];
                        int offset = 0;
                        foreach (var memory in memoryGroup)
                        {
                            var chunkSpan = MemoryMarshal.AsBytes(memory.Span);
                            chunkSpan.CopyTo(pixelDataArray.AsSpan(offset));
                            offset += chunkSpan.Length;
                        }

                        glTexture = new GLTexture(gl, image.Width, image.Height, pixelDataArray);
                    }

                    var texture = new Texture(glTexture);

                    textureCache[path] = texture;
                    return texture;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load texture '{path}': {ex.Message}");
            textureCache[path] = missingTexture;
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
            // Only dispose textures that aren't the shared WhitePixel or Missing texture
            if (tex != WhitePixel && tex.GlTexture.Handle != missingTexture.GlTexture.Handle)
            {
                tex.GlTexture.Dispose();
            }
        }

        textureCache.Clear();
    }
}
