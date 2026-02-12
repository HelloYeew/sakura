// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sakura.Framework.Graphics.Textures;

public class HeadlessTextureManager : ITextureManager
{
    public HeadlessTextureManager()
    {
        WhitePixel = createDummyTexture(1, 1);
    }

    public Texture WhitePixel { get; }

    public Texture Get(string path)
    {
        return WhitePixel;
    }

    public Texture FromPixelData(int width, int height, ReadOnlySpan<byte> pixelData)
    {
        return createDummyTexture(width, height);
    }

    public void Dispose()
    {

    }

    /// <summary>
    /// Creates a dummy <see cref="Texture"/> (that has <see cref="GLTexture"/> behind) without touching OpenGL.
    /// </summary>
    /// <param name="width">Width of the dummy texture.</param>
    /// <param name="height">Height of the dummy texture.</param>
    /// <returns>A <see cref="Texture"/> instance that appears valid but does not consume GPU resources.</returns>
    private Texture createDummyTexture(int width, int height)
    {
        var dummyGlTexture = (GLTexture)RuntimeHelpers.GetUninitializedObject(typeof(GLTexture));

        // Set Width/Height via reflection
        typeof(GLTexture)
            .GetField("<Width>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.SetValue(dummyGlTexture, width);

        typeof(GLTexture)
            .GetField("<Height>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.SetValue(dummyGlTexture, height);

        // Set Handle to something non-zero so it looks valid
        typeof(GLTexture)
            .GetField("<Handle>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.SetValue(dummyGlTexture, (uint)1);

        // Since we skipped the ctor of texture class, the 'gl' field is null.
        // If the code tries to Dispose this texture later, 'gl.DeleteTexture' would crash.
        // Setting disposed = true makes the Dispose() method return immediately.
        typeof(GLTexture)
            .GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(dummyGlTexture, true);

        return new Texture(dummyGlTexture);
    }
}
