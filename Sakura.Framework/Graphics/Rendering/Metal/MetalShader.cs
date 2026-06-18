// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering.Metal;

/// <summary>
/// A Metal shader program: a set of <c>MTLRenderPipelineState</c> variants (one per
/// <see cref="BlendingMode"/>, since Metal bakes blend state into the pipeline) built from
/// cross-compiled MSL plus the vertex layout.
/// Uniforms travel as std140 blocks. For the main shader the renderer uploads the projection/mask
/// blocks directly. for effect shaders (blur/grayscale) the buffered-container draw node calls
/// <see cref="SetUniformBlock{T}"/> by name, which this class maps to the right stage + MSL buffer
/// index and uploads via the device. The name -> binding map is supplied at construction (the indices
/// are deterministic from the sakura-spirv resource ordering — see MetalRenderer for the derivation).
/// </summary>
public sealed class MetalShader : IShader
{
    /// <summary>
    /// The stage a uniform block is bound on, for <see cref="UniformBinding"/>.
    /// </summary>
    public enum Stage
    {
        Vertex,
        Fragment,
    }

    /// <summary>
    /// Where a named uniform block lands in the cross-compiled MSL.
    /// </summary>
    public readonly record struct UniformBinding(Stage Stage, int BufferIndex);

    private readonly nint device;
    private readonly string vertexMsl;
    private readonly string fragmentMsl;
    private readonly MetalVertexAttribute[] attributes;
    private readonly int vertexStride;

    // Set for shaders created via MetalRenderer.CreateShader, so Use() can route pipeline binding back
    // through the renderer (which owns the current blend mode → pipeline-variant selection). Null for
    // the main shader, which the renderer binds directly.
    private readonly MetalRenderer owner;

    // Pipeline state per blend mode, created lazily (Metal bakes blend into the pipeline).
    private readonly nint[] pipelines = new nint[6];

    private readonly Dictionary<string, UniformBinding> uniformBlockBindings;

    /// <summary>
    /// The default (alpha-blend) pipeline handle, for callers that don't vary blend.
    /// </summary>
    public nint Handle => GetPipeline(BlendingMode.Alpha);

    public MetalShader(nint device, string vertexMsl, string fragmentMsl,
        ReadOnlySpan<MetalVertexAttribute> attributes, int vertexStride,
        IReadOnlyDictionary<string, UniformBinding> uniformBlockBindings = null,
        MetalRenderer owner = null)
    {
        this.device = device;
        this.vertexMsl = vertexMsl;
        this.fragmentMsl = fragmentMsl;
        this.attributes = attributes.ToArray();
        this.vertexStride = vertexStride;
        this.owner = owner;
        this.uniformBlockBindings = uniformBlockBindings != null
            ? new Dictionary<string, UniformBinding>(uniformBlockBindings)
            : new Dictionary<string, UniformBinding>();

        // Eagerly create the alpha variant so construction fails fast on a bad MSL compile (matching
        // the previous behaviour where the single pipeline was built in the constructor).
        GetPipeline(BlendingMode.Alpha);
    }

    /// <summary>
    /// Returns the pipeline state for the given blend mode, creating it on first use.
    /// </summary>
    public unsafe nint GetPipeline(BlendingMode blendMode)
    {
        int i = (int)blendMode;
        if (pipelines[i] != nint.Zero)
            return pipelines[i];

        fixed (MetalVertexAttribute* attrPtr = attributes)
        {
            pipelines[i] = SakuraMetalNative.sakura_metal_create_pipeline(
                device, vertexMsl, fragmentMsl, attrPtr, attributes.Length, vertexStride, i);
        }

        if (pipelines[i] == nint.Zero)
            throw new InvalidOperationException($"Failed to create Metal pipeline state from MSL (blend {blendMode}).");

        return pipelines[i];
    }

    /// <summary>
    /// Makes this the renderer's current shader, binding its pipeline variant for the renderer's
    /// current blend mode. For the main shader (no owner) this is a no-op — the renderer binds it
    /// directly via <c>sakura_metal_set_pipeline</c>.
    /// </summary>
    public void Use() => owner?.UseShader(this);

    // Per-name scalar uniforms aren't used by the Metal path (everything travels as blocks). The one
    // exception, effect shaders' `u_Texture` sampler index, is implicit on Metal (the texture is bound
    // to slot 0 by the renderer), so this is a safe no-op.
    public void SetUniform(string name, int value) { }
    public void SetUniform(string name, float value) { }
    public void SetUniform(string name, bool value) { }
    public void SetUniform(string name, Matrix4x4 value) { }
    public void SetUniform(string name, Vector2 value) { }
    public void SetUniform(string name, Vector4 value) { }
    public void SetUniform(string name, Color value) { }
    public void SetUniformIntArray(string name, int[] values) { }
    public void SetUniform(string name, float[] mat3X3) { }

    /// <summary>
    /// Uploads a std140 uniform block by name to the stage + MSL buffer index recorded at
    /// construction. Used by the effect passes (ProjectionBlock, BlurBlock, GrayscaleBlock).
    /// </summary>
    public unsafe void SetUniformBlock<T>(string blockName, in T data) where T : unmanaged
    {
        if (!uniformBlockBindings.TryGetValue(blockName, out var binding))
            return; // Unknown block on this shader, nothing to bind.

        fixed (T* ptr = &data)
        {
            if (binding.Stage == Stage.Vertex)
                SakuraMetalNative.sakura_metal_set_vertex_uniform(device, ptr, sizeof(T), binding.BufferIndex);
            else
                SakuraMetalNative.sakura_metal_set_fragment_uniform(device, ptr, sizeof(T), binding.BufferIndex);
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < pipelines.Length; i++)
        {
            if (pipelines[i] != nint.Zero)
            {
                SakuraMetalNative.sakura_metal_destroy_pipeline(pipelines[i]);
                pipelines[i] = nint.Zero;
            }
        }
    }
}
