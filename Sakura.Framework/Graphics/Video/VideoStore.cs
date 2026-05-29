// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Retrieves <see cref="VideoDecoder"/> instances from a <see cref="Storage"/>.
/// Supports embedded resources, application support folders, or any other storage.
/// </summary>
public class VideoStore
{
    private readonly Storage storage;
    private readonly IRenderer renderer;
    private readonly GL gl;
    private readonly ITextureManager textureManager;

    public VideoStore(Storage storage, IRenderer renderer, GL gl, ITextureManager textureManager)
    {
        this.storage = storage;
        this.renderer = renderer;
        this.gl = gl;
        this.textureManager = textureManager;
    }

    /// <summary>
    /// Creates a new <see cref="VideoDecoder"/> for the given filename.
    /// Each call returns a fresh, independent decoder (video decoders are stateful).
    /// Returns <c>null</c> if the file does not exist in storage.
    /// </summary>
    public VideoDecoder? GetDecoder(string name)
    {
        if (!storage.Exists(name))
            return null;

        Stream stream = storage.GetStream(name, FileAccess.Read, FileMode.Open);
        return new VideoDecoder(renderer, gl, textureManager, stream);
    }
}
