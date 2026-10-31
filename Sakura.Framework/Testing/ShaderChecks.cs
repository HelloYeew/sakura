// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Platform;
using Sakura.Framework.SPIRV;

namespace Sakura.Framework.Testing;

/// <summary>
/// For a game's tests: whether its own shaders will work on every backend, including the ones the machine running the tests
/// doesn't have.
/// </summary>
/// <remarks>
/// A shader that won't build only shows when it's first drawn, and then only on the backend it fails on, as something silently
/// missing (see <see cref="CustomShaderStore"/>). Everything here is compiled for real, so it takes tens of milliseconds a shader.
/// </remarks>
public static partial class ShaderChecks
{
    private static readonly Storage framework_shaders = new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources.Shaders");

    [GeneratedRegex(@":\s*TEXCOORD(\d+)\s*;")]
    private static partial Regex inputPattern();

    /// <summary>
    /// What's wrong with a shader, on any backend. Empty if nothing is.
    /// </summary>
    /// <param name="shaders">Where the game's shaders are, as given to <see cref="CustomShaderStore"/>.</param>
    /// <param name="fragmentPath">The fragment shader, within them.</param>
    /// <param name="vertexPath">The vertex shader, if not the framework's.</param>
    public static IReadOnlyList<string> Problems(Storage shaders, string fragmentPath, string vertexPath = CustomShaderStore.DEFAULT_VERTEX_SHADER)
    {
        var problems = new List<string>();
        Storage sources = new CompositeStorage(shaders, framework_shaders);

        foreach (CrossCompileTarget target in new[] { CrossCompileTarget.GLSL, CrossCompileTarget.HLSL, CrossCompileTarget.MSL })
        {
            try
            {
                if (target == CrossCompileTarget.GLSL)
                {
                    ShaderCompiler.GetOrCompile(sources, vertexPath, fragmentPath, target);
                    continue;
                }

                (_, string fragment, _) = ShaderCompiler.GetOrCompileWithBlocks(sources, vertexPath, fragmentPath, target);

                if (target == CrossCompileTarget.HLSL)
                    problems.AddRange(droppedInputs(fragment));
            }
            catch (Exception e)
            {
                problems.Add($"{target}: {e.Message}");
            }
        }

        try
        {
            SpirvShaderPair spirv = ShaderCompiler.GetOrCompileSpirv(sources, vertexPath, fragmentPath);
            VulkanShaderLayout.Create(SpirvReflection.Parse(spirv.VertexForReflection), SpirvReflection.Parse(spirv.FragmentForReflection), $"{vertexPath}+{fragmentPath}");
        }
        catch (Exception e)
        {
            problems.Add($"Vulkan: {e.Message}");
        }

        return problems;
    }

    /// <summary>
    /// Direct3D links a fragment shader's inputs to the vertex shader's outputs by register, not by name. Stopping early is
    /// fine, but an input skipped before one that's read would move that one, and all after it, onto the wrong output. The
    /// cross-compiler skips any the shader never reads, without complaint; <c>sh_Custom.glsl</c> reads them all.
    /// </summary>
    private static IEnumerable<string> droppedInputs(string fragmentHlsl)
    {
        var inputs = inputPattern().Matches(fragmentHlsl).Select(match => int.Parse(match.Groups[1].Value)).ToHashSet();

        if (inputs.Count == 0)
            yield break;

        for (int input = 0; input < inputs.Max(); input++)
        {
            if (!inputs.Contains(input))
                yield return $"Direct3D: it never reads the input at location {input}, which moves the later ones onto the wrong outputs. Read it, or end with writeFragment from sh_Custom.glsl.";
        }
    }
}
