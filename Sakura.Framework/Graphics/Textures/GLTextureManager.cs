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
using SixLabors.ImageSharp.Processing;

namespace Sakura.Framework.Graphics.Textures;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLTextureManager : ITextureManager
{
    // TODO: Centralize texture management across different graphics backends. (ITextureManager?)
    private readonly GL gl;
    private readonly Storage storage;
    private readonly Dictionary<string, Texture> textureCache = new();

    /// <summary>
    /// A 1x1 white pixel texture.
    /// </summary>
    public Texture WhitePixel { get; }

    public GLTextureManager(GL gl, Storage storage)
    {
        this.gl = gl;
        this.storage = storage;

        // Wrap the static TextureGL.WhitePixel in a public-facing Texture.
        WhitePixel = new Texture(TextureGL.WhitePixel);
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
            // Load the image using ImageSharp
            using (var stream = storage.GetStream(path))
            {
                if (stream == null)
                    throw new FileNotFoundException($"Image file not found at storage path: {path}");

                // Load the image using ImageSharp
                using (var image = Image.Load<Rgba32>(stream))
                {
                    // ImageSharp loads images "upside down" relative to OpenGL's expectations.
                    // We need to flip it vertically before uploading.
                    image.Mutate(x => x.Flip(FlipMode.Vertical));

                    // Get pixel data.
                    // We need a contiguous block of memory to upload to OpenGL.
                    // GetPixelMemoryGroup() returns one or more memory blocks.
                    var memoryGroup = image.GetPixelMemoryGroup();

                    TextureGL textureGl;

                    if (memoryGroup.Count == 1)
                    {
                        // The image is contiguous in memory. We can use its span directly.
                        var pixelDataSpan = MemoryMarshal.AsBytes(memoryGroup[0].Span);
                        textureGl = new TextureGL(gl, image.Width, image.Height, pixelDataSpan);
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

                        textureGl = new TextureGL(gl, image.Width, image.Height, pixelDataArray);
                    }

                    // Create a public-facing Texture that points to the *whole* TextureGL
                    var texture = new Texture(textureGl);

                    textureCache[path] = texture;
                    return texture;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load texture '{path}': {ex.Message}");
            // Return a fallback "missing" texture
            return createMissingTexture(path);
        }
    }

    /// <summary>
    /// Creates a fallback magenta/black checkerboard texture to indicate a missing asset.
    /// </summary>
    private Texture createMissingTexture(string path)
    {
        if (textureCache.TryGetValue("!!MISSING!!", out var missingTex))
            return missingTex;

        const int size = 64;
        byte[] data = new byte[size * size * 4];
        for(int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool xOdd = (x / (size / 2)) % 2 == 1;
            bool yOdd = (y / (size / 2)) % 2 == 1;
            bool isMagenta = xOdd ^ yOdd;

            int offset = (y * size + x) * 4;
            if (isMagenta)
            {
                data[offset] = 255;     // R
                data[offset + 1] = 0;   // G
                data[offset + 2] = 255; // B
                data[offset + 3] = 255; // A
            }
            else
            {
                data[offset] = 0;       // R
                data[offset + 1] = 0;   // G
                data[offset + 2] = 0;   // B
                data[offset + 3] = 255; // A
            }
        }

        var gl = new TextureGL(this.gl, size, size, data);
        var tex = new Texture(gl);

        textureCache["!!MISSING!!"] = tex;
        textureCache[path] = tex; // Also cache it under the path that failed
        return tex;
    }

    public void Dispose()
    {
        foreach (var tex in textureCache.Values)
        {
            // Only dispose textures that aren't the shared WhitePixel or Missing texture
            if (tex != WhitePixel && tex.TextureGL.Handle != (textureCache.GetValueOrDefault("!!MISSING!!")?.TextureGL.Handle ?? 0))
            {
                tex.TextureGL.Dispose();
            }
        }

        textureCache.Clear();
    }
}
