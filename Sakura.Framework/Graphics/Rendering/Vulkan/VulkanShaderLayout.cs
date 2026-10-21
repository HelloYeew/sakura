// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// The resource layout of a vertex + fragment shader pair, checked against the one layout every
/// pipeline of the Vulkan renderer shares.
/// </summary>
/// <remarks>
/// <para>
/// The renderer doesn't build a pipeline layout per shader. Every pipeline uses the same set 0, which
/// therefore stays bound across shader switches, and one of two set-1 layouts:
/// </para>
/// <list type="bullet">
/// <item><description>Set 0, bindings 0–7: uniform blocks, all dynamic uniform buffers. The framework
/// uses 0 (ProjectionBlock), 1 (MaskBlock), 2 (GrayscaleBlock), 3 (BlurBlock) and 4 (VideoBlock); 5–7 are
/// left for game shaders. Eight is the most dynamic uniform buffers the spec guarantees.</description></item>
/// <item><description>Set 1, binding 0: one array of up to 16 textures (the main shader), or set 1,
/// bindings 0–3: up to four single textures (effect, video and most game shaders). There are two
/// layouts because a single one declaring both would put 19 samplers on the fragment stage, and the
/// spec only guarantees 16.</description></item>
/// </list>
/// <para>
/// Push constants, storage buffers and storage or separate images have no place in that layout, so a
/// shader using one is rejected here, by name, instead of failing inside the driver.
/// </para>
/// </remarks>
internal sealed class VulkanShaderLayout
{
    public const uint UNIFORM_SET = 0;
    public const uint TEXTURE_SET = 1;
    public const uint UNIFORM_BINDING_COUNT = 8;

    /// <summary>
    /// The bytes every uniform-block descriptor covers, and so the largest block a shader may declare.
    /// Blocks are written at a fresh offset of the frame's stream buffer each time they change, and the
    /// descriptor's range has to be fixed when the set is written.
    /// </summary>
    public const uint UNIFORM_BLOCK_RANGE = 256;
    public const uint TEXTURE_ARRAY_LENGTH = 16;
    public const uint SINGLE_TEXTURE_COUNT = 4;

    /// <summary>
    /// The vertex field each input location reads and its component count. Every backend maps
    /// locations to fields this way.
    /// </summary>
    public static readonly IReadOnlyList<(string Field, uint Components)> VertexAttributes =
    [
        (nameof(SakuraVertex.Position), 2),
        (nameof(SakuraVertex.TexCoords), 2),
        (nameof(SakuraVertex.Color), 4),
        (nameof(SakuraVertex.TexIndex), 1),
        (nameof(SakuraVertex.ClipData), 4),
        (nameof(SakuraVertex.ClipShearX), 1),
        (nameof(SakuraVertex.ClipRadius), 1),
    ];

    /// <summary>
    /// The uniform blocks either stage declares, by block name.
    /// </summary>
    public IReadOnlyDictionary<string, SpirvUniformBlock> UniformBlocks { get; }

    /// <summary>
    /// Whether textures are an array at set 1, binding 0 (texture slot <c>i</c> is array element
    /// <c>i</c>), rather than single textures at set 1, bindings 0–3 (texture slot <c>i</c> is binding
    /// <c>i</c>).
    /// </summary>
    public bool UsesTextureArray { get; }

    public IReadOnlyList<SpirvSampler> Samplers { get; }

    public IReadOnlyList<SpirvVertexInput> VertexInputs { get; }

    private VulkanShaderLayout(IReadOnlyDictionary<string, SpirvUniformBlock> uniformBlocks, bool usesTextureArray, IReadOnlyList<SpirvSampler> samplers, IReadOnlyList<SpirvVertexInput> vertexInputs)
    {
        UniformBlocks = uniformBlocks;
        UsesTextureArray = usesTextureArray;
        Samplers = samplers;
        VertexInputs = vertexInputs;
    }

    /// <summary>
    /// Merges the two stages' reflection and checks it against the shared layout.
    /// </summary>
    /// <param name="vertex">Reflection of the vertex stage's SPIR-V.</param>
    /// <param name="fragment">Reflection of the fragment stage's SPIR-V.</param>
    /// <param name="name">Names the shader pair in the exception, e.g. "shader.vert+blur.frag".</param>
    /// <exception cref="InvalidOperationException">The pair doesn't fit the layout. The message lists
    /// every violation.</exception>
    public static VulkanShaderLayout Create(SpirvReflection vertex, SpirvReflection fragment, string name)
    {
        var problems = new List<string>();

        if (vertex.Stage != SpirvStage.Vertex)
            problems.Add($"the vertex module's entry point is a {vertex.Stage} shader");
        if (fragment.Stage != SpirvStage.Fragment)
            problems.Add($"the fragment module's entry point is a {fragment.Stage} shader");

        var blocks = mergeUniformBlocks(vertex, fragment, problems);
        bool usesTextureArray = checkSamplers(vertex, fragment, problems);
        checkVertexInputs(vertex, problems);

        foreach (var module in new[] { vertex, fragment })
        {
            if (module.HasPushConstants)
                problems.Add($"the {module.Stage} stage uses push constants; use a uniform block in set {UNIFORM_SET} instead");

            foreach (var other in module.OtherResources)
                problems.Add($"'{other.Name}' (set {other.Set}, binding {other.Binding}) is a {describe(other.Kind)}, which the renderer never binds");
        }

        if (problems.Count > 0)
            throw new InvalidOperationException($"Shader '{name}' doesn't fit the Vulkan resource layout:\n- {string.Join("\n- ", problems)}");

        return new VulkanShaderLayout(blocks, usesTextureArray, fragment.Samplers, vertex.VertexInputs);
    }

    private static Dictionary<string, SpirvUniformBlock> mergeUniformBlocks(SpirvReflection vertex, SpirvReflection fragment, List<string> problems)
    {
        var byName = new Dictionary<string, SpirvUniformBlock>();
        var byBinding = new Dictionary<uint, SpirvUniformBlock>();

        foreach (var block in vertex.UniformBlocks.Concat(fragment.UniformBlocks))
        {
            if (block.Set != UNIFORM_SET || block.Binding >= UNIFORM_BINDING_COUNT)
            {
                problems.Add($"uniform block '{block.Name}' is at set {block.Set}, binding {block.Binding}; uniform blocks must be in set {UNIFORM_SET}, bindings 0–{UNIFORM_BINDING_COUNT - 1}");
                continue;
            }

            if (block.Size > UNIFORM_BLOCK_RANGE)
            {
                problems.Add($"uniform block '{block.Name}' is {block.Size} bytes; a uniform block may be at most {UNIFORM_BLOCK_RANGE} bytes");
                continue;
            }

            if (byBinding.TryGetValue(block.Binding, out var existing))
            {
                // The same block seen from the other stage.
                if (existing.Name == block.Name && existing.Size == block.Size)
                    continue;

                problems.Add($"uniform blocks '{existing.Name}' ({existing.Size} bytes) and '{block.Name}' ({block.Size} bytes) share set {UNIFORM_SET}, binding {block.Binding}");
                continue;
            }

            if (byName.TryGetValue(block.Name, out existing))
            {
                problems.Add($"uniform block '{block.Name}' is declared at both binding {existing.Binding} and binding {block.Binding}");
                continue;
            }

            byBinding[block.Binding] = block;
            byName[block.Name] = block;
        }

        return byName;
    }

    private static bool checkSamplers(SpirvReflection vertex, SpirvReflection fragment, List<string> problems)
    {
        foreach (var sampler in vertex.Samplers)
            problems.Add($"texture '{sampler.Name}' is sampled in the vertex stage; textures are only bound to the fragment stage");

        var samplers = fragment.Samplers;
        bool usesTextureArray = samplers.Any(s => s.ArrayLength > 1);

        foreach (var sampler in samplers)
        {
            if (sampler.Set != TEXTURE_SET)
            {
                problems.Add($"texture '{sampler.Name}' is at set {sampler.Set}, binding {sampler.Binding}; textures must be in set {TEXTURE_SET}");
                continue;
            }

            if (usesTextureArray)
            {
                if (samplers.Count > 1)
                    problems.Add($"texture '{sampler.Name}' is declared next to a texture array; a shader uses either one array of up to {TEXTURE_ARRAY_LENGTH} textures at binding 0, or up to {SINGLE_TEXTURE_COUNT} single textures");
                else if (sampler.Binding != 0 || sampler.ArrayLength > TEXTURE_ARRAY_LENGTH)
                    problems.Add($"texture array '{sampler.Name}[{sampler.ArrayLength}]' is at binding {sampler.Binding}; a texture array must be at binding 0 with at most {TEXTURE_ARRAY_LENGTH} elements");
            }
            else if (sampler.Binding >= SINGLE_TEXTURE_COUNT)
                problems.Add($"texture '{sampler.Name}' is at binding {sampler.Binding}; single textures must be at bindings 0–{SINGLE_TEXTURE_COUNT - 1}");
        }

        return usesTextureArray;
    }

    private static void checkVertexInputs(SpirvReflection vertex, List<string> problems)
    {
        foreach (var input in vertex.VertexInputs)
        {
            if (input.Location >= VertexAttributes.Count)
            {
                problems.Add($"vertex input '{input.Name}' is at location {input.Location}; the vertex format has locations 0–{VertexAttributes.Count - 1}");
                continue;
            }

            var attribute = VertexAttributes[(int)input.Location];

            if (input.ComponentKind != SpirvScalarKind.Float || input.ComponentCount > attribute.Components)
                problems.Add($"vertex input '{input.Name}' at location {input.Location} reads {input.ComponentCount} {input.ComponentKind} components, but the location carries {attribute.Field}: {attribute.Components} float components");
        }
    }

    private static string describe(SpirvOtherResourceKind kind) => kind switch
    {
        SpirvOtherResourceKind.StorageBuffer => "storage buffer",
        SpirvOtherResourceKind.Image => "storage or separate image",
        SpirvOtherResourceKind.Sampler => "separate sampler",
        _ => "resource of an unsupported type",
    };
}
