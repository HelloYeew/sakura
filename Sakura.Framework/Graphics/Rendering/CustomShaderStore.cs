// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A game's own shaders, built for whichever renderer draws them, for a <see cref="CustomShaderDrawNode"/> to draw with.
/// </summary>
/// <remarks>
/// <para>A shader is GLSL 450, compiled for every backend. It's read from the storage given here, and so are its
/// <c>#include</c>s, with the framework's shaders behind it: a fragment shader pairs with the framework's
/// <c>shader.vert</c> by default, and starts from <c>sh_Custom.glsl</c>.</para>
/// <para>Only from the draw thread. A shader that won't build is logged once and comes back null, so whatever draws with
/// it is left out rather than breaking the frame.</para>
/// </remarks>
public sealed class CustomShaderStore
{
    /// <summary>
    /// The vertex shader a fragment shader pairs with unless it's told otherwise.
    /// </summary>
    public const string DEFAULT_VERTEX_SHADER = "shader.vert";

    private readonly Storage shaders;
    private readonly Dictionary<(string Vertex, string Fragment), IShader?> built = new Dictionary<(string, string), IShader?>();
    private IRenderer? builtFor;

    /// <param name="shaders">Where the game's shaders are (an <see cref="EmbeddedResourceStorage"/> over its resources).</param>
    public CustomShaderStore(Storage shaders)
    {
        this.shaders = shaders;
    }

    /// <summary>
    /// The shader drawing with <paramref name="fragmentPath"/> on <paramref name="renderer"/>, built the first time it's asked
    /// for; null if it won't build.
    /// </summary>
    public IShader? Get(ICustomShaderRenderer renderer, string fragmentPath, string vertexPath = DEFAULT_VERTEX_SHADER)
    {
        // A new renderer (the backend switched) needs its own; the old ones went with the old renderer.
        if (!ReferenceEquals(renderer, builtFor))
        {
            built.Clear();
            builtFor = renderer;
        }

        if (built.TryGetValue((vertexPath, fragmentPath), out IShader? shader))
            return shader;

        try
        {
            shader = renderer.CreateShader(Sources(renderer.ShaderStorage), vertexPath, fragmentPath);
        }
        catch (Exception e)
        {
            Logger.Error($"Couldn't build the {vertexPath}+{fragmentPath} shader, so what draws with it is left out.", e);
            shader = null;
        }

        built[(vertexPath, fragmentPath)] = shader;
        return shader;
    }

    /// <summary>
    /// Where a shader's files are read from: the game's, then <paramref name="frameworkShaders"/>.
    /// </summary>
    public Storage Sources(Storage frameworkShaders) => new CompositeStorage(shaders, frameworkShaders);
}
