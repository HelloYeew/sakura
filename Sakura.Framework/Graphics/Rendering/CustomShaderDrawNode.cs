// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering.Uniforms;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Draws a drawable's vertices with a shader of the game's own rather than the framework's: a drawable returns one from
/// <c>CreateDrawNode</c>. It stays inside any mask the drawable is in, as long as the shader does what
/// <c>sh_Custom.glsl</c> does.
/// </summary>
/// <remarks>
/// Nothing is drawn where the shader can't be: headless, or where it won't build (see <see cref="CustomShaderStore"/>). Anything
/// that must show regardless needs a layer of its own that doesn't use one.
/// </remarks>
public class CustomShaderDrawNode : DrawNode
{
    private readonly CustomShaderStore shaders;
    private readonly string fragmentPath;
    private readonly string vertexPath;

    /// <param name="shaders">The game's shaders.</param>
    /// <param name="fragmentPath">The fragment shader, within them.</param>
    /// <param name="vertexPath">The vertex shader, if not the framework's.</param>
    public CustomShaderDrawNode(CustomShaderStore shaders, string fragmentPath, string vertexPath = CustomShaderStore.DEFAULT_VERTEX_SHADER)
    {
        this.shaders = shaders;
        this.fragmentPath = fragmentPath;
        this.vertexPath = vertexPath;
    }

    /// <summary>
    /// Uploads whatever uniform blocks <paramref name="shader"/> takes, other than <c>ProjectionBlock</c>, which is already
    /// done. On the draw thread, so it reads a copy taken in <see cref="DrawNode.ApplyState"/>. For one block,
    /// <see cref="CustomShaderDrawNode{TBlock}"/> does all this.
    /// </summary>
    protected virtual void SetUniforms(IShader shader)
    {
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || VertexCount == 0 || renderer is not ICustomShaderRenderer custom)
            return;

        IShader? shader = shaders.Get(custom, fragmentPath, vertexPath);

        if (shader == null)
            return;

        CountDrawn();

        renderer.SetBlendMode(Blending);
        renderer.FlushBatch();

        shader.Use();
        shader.SetUniformBlock("ProjectionBlock", new ProjectionBlock { Projection = renderer.ProjectionMatrix });
        SetUniforms(shader);

        renderer.ApplyCurrentClip(WritableVertices);
        custom.DrawVerticesRaw(Vertices);
        renderer.RestoreMainShader();
    }
}

/// <summary>
/// A <see cref="CustomShaderDrawNode"/> whose shader takes one uniform block of its own, read from the drawable each time
/// its state is applied.
/// </summary>
/// <typeparam name="TBlock">
/// The block, matching the shader's <c>std140</c> declaration field for field (<see cref="Uniforms.ProjectionBlock"/> is an
/// example of how to pin one). A shader's blocks belong at set 0, bindings 5–7, and at most 256 bytes.
/// </typeparam>
public class CustomShaderDrawNode<TBlock> : CustomShaderDrawNode
    where TBlock : unmanaged
{
    private readonly string blockName;
    private readonly Func<TBlock> read;
    private TBlock block;

    /// <param name="shaders">The game's shaders.</param>
    /// <param name="fragmentPath">The fragment shader, within them.</param>
    /// <param name="blockName">The name the shader declares the block with.</param>
    /// <param name="read">Gives the block's values, on the update thread.</param>
    /// <param name="vertexPath">The vertex shader, if not the framework's.</param>
    /// <exception cref="ArgumentException"><typeparamref name="TBlock"/> isn't a whole number of 16 bytes.</exception>
    public unsafe CustomShaderDrawNode(CustomShaderStore shaders, string fragmentPath, string blockName, Func<TBlock> read, string vertexPath = CustomShaderStore.DEFAULT_VERTEX_SHADER)
        : base(shaders, fragmentPath, vertexPath)
    {
        // std140 rounds a whole block up to this, and Metal checks that what's uploaded covers it. A struct that stops at its
        // last field works everywhere else, then fails on one backend only.
        if (sizeof(TBlock) % 16 != 0)
            throw new ArgumentException($"{typeof(TBlock).Name} is {sizeof(TBlock)} bytes, and a uniform block has to be a whole number of 16 bytes: pad it.", nameof(TBlock));

        this.blockName = blockName;
        this.read = read;
    }

    public override void ApplyState(Drawable source)
    {
        base.ApplyState(source);
        block = read();
    }

    protected override void SetUniforms(IShader shader) => shader.SetUniformBlock(blockName, block);
}
