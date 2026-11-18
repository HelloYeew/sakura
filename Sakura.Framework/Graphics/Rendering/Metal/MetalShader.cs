// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering.Metal;

/// <summary>
/// A Metal shader program: a set of <c>MTLRenderPipelineState</c> variants (one per
/// <see cref="BlendingMode"/>, since Metal bakes blend state into the pipeline) built from
/// cross-compiled MSL plus the vertex layout.
/// Uniforms travel as std140 blocks. For the main shader the renderer uploads the projection/mask
/// blocks directly. For any other shader, a draw node calls <see cref="SetUniformBlock{T}"/> by name, which
/// this class maps to the right stage + MSL buffer index and uploads via the device. The name -> binding
/// map is supplied at construction: MSL doesn't keep a block's name, so <see cref="ShaderUniformBlocks"/>
/// works it out when the shader is compiled.
/// </summary>
public sealed class MetalShader : IShader
{
    /// <summary>
    /// The stages a uniform block is bound on, for <see cref="UniformBinding"/>.
    /// </summary>
    [Flags]
    public enum Stage
    {
        Vertex = 1,
        Fragment = 2,
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

    // Pipeline state per blend mode (Metal bakes blend into the pipeline): built at first use, or ahead of it by
    // PrepareVariants on a pool thread. Either side publishes with a compare-exchange, so a variant both build is kept once.
    private readonly nint[] pipelines = new nint[6];

    private Task preparing;
    private volatile bool disposed;

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
    /// <exception cref="InvalidOperationException">Metal couldn't build the pipeline.</exception>
    public nint GetPipeline(BlendingMode blendMode)
    {
        int i = (int)blendMode;
        nint pipeline = Volatile.Read(ref pipelines[i]);

        return pipeline != nint.Zero ? pipeline : publish(i, createPipeline(i));
    }

    /// <summary>
    /// Builds every blend variant not built yet on a pool thread, so the first draw in a mode doesn't wait for Metal to
    /// compile it. Does nothing once started, or after <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Draw thread only. A variant that fails here is left for its first use, which throws as it always has.
    /// </remarks>
    public void PrepareVariants()
    {
        if (disposed || preparing != null)
            return;

        preparing = Task.Run(prepareVariants);
    }

    /// <summary>
    /// Builds the variants still missing, on the pool thread <see cref="PrepareVariants"/> started, until disposed.
    /// </summary>
    private void prepareVariants()
    {
        for (int i = 0; i < pipelines.Length && !disposed; i++)
        {
            if (Volatile.Read(ref pipelines[i]) != nint.Zero)
                continue;

            try
            {
                publish(i, createPipeline(i));
            }
            catch (InvalidOperationException e)
            {
                Logger.Verbose($"Metal: {e.Message} It will be tried again when it's first drawn.");
            }
        }
    }

    /// <summary>
    /// Keeps <paramref name="pipeline"/> as variant <paramref name="index"/> unless another thread got there first, in
    /// which case it's destroyed (nothing has drawn with it) and the other one is returned.
    /// </summary>
    private nint publish(int index, nint pipeline)
    {
        nint existing = Interlocked.CompareExchange(ref pipelines[index], pipeline, nint.Zero);

        if (existing == nint.Zero)
            return pipeline;

        SakuraMetalNative.sakura_metal_destroy_pipeline(pipeline);
        return existing;
    }

    /// <summary>
    /// Builds the pipeline for blend mode <paramref name="index"/>. Safe on any thread: the native side guards its
    /// compiled-function cache, and a Metal device builds pipelines from any thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">Metal couldn't build the pipeline.</exception>
    private unsafe nint createPipeline(int index)
    {
        nint pipeline;

        fixed (MetalVertexAttribute* attrPtr = attributes)
            pipeline = SakuraMetalNative.sakura_metal_create_pipeline(device, vertexMsl, fragmentMsl, attrPtr, attributes.Length, vertexStride, index);

        if (pipeline == nint.Zero)
            throw new InvalidOperationException($"Failed to create Metal pipeline state from MSL (blend {(BlendingMode)index}).");

        return pipeline;
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
    /// Uploads a std140 uniform block by name to the stages + MSL buffer index recorded at
    /// construction.
    /// </summary>
    public unsafe void SetUniformBlock<T>(string blockName, in T data) where T : unmanaged
    {
        if (!uniformBlockBindings.TryGetValue(blockName, out var binding))
            return; // A block this shader doesn't declare or never reads: nothing to bind.

        fixed (T* ptr = &data)
        {
            if ((binding.Stage & Stage.Vertex) != 0)
                SakuraMetalNative.sakura_metal_set_vertex_uniform(device, ptr, sizeof(T), binding.BufferIndex);

            if ((binding.Stage & Stage.Fragment) != 0)
                SakuraMetalNative.sakura_metal_set_fragment_uniform(device, ptr, sizeof(T), binding.BufferIndex);
        }
    }

    /// <summary>
    /// Destroys every variant, after waiting for any still being built ahead of use.
    /// </summary>
    public void Dispose()
    {
        disposed = true;

        try
        {
            preparing?.Wait();
        }
        catch (AggregateException)
        {
            // Only Metal's own failures reach here, and those are logged where they happen.
        }

        for (int i = 0; i < pipelines.Length; i++)
        {
            nint pipeline = Interlocked.Exchange(ref pipelines[i], nint.Zero);

            if (pipeline != nint.Zero)
                SakuraMetalNative.sakura_metal_destroy_pipeline(pipeline);
        }
    }
}
