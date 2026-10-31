// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sakura.Framework.Graphics.Rendering.Vulkan;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Where a uniform block of a vertex + fragment pair is bound once cross-compiled to HLSL or MSL.
/// </summary>
/// <param name="Name">The name the shader declares it with.</param>
/// <param name="InVertex">Whether the vertex stage reads it.</param>
/// <param name="InFragment">Whether the fragment stage reads it.</param>
/// <param name="Index">Its Metal buffer index or Direct3D constant buffer register, the same in both stages.</param>
internal readonly record struct ShaderUniformBlock(string Name, bool InVertex, bool InFragment, int Index);

/// <summary>
/// Finds <see cref="ShaderUniformBlock"/>s, for the backends whose cross-compiled source doesn't keep a block's name.
/// </summary>
/// <remarks>
/// <para>The cross-compiler numbers a pair's uniform blocks in order of set then binding, from one counter across both
/// stages, leaving out any block the optimizer removed because nothing reads it. So how a block is numbered depends on
/// which others survived, and only the optimized modules know that: counting what the source declares would put every
/// later block one off the moment one goes unread, and upload it to the wrong place without a word.</para>
/// <para>Those modules have no names left, so names come from the source. Compiling a named copy would say the same for
/// another 40 ms a stage, on the draw thread, for what a few regular expressions can read.</para>
/// <para>Storage buffers would take numbers from the same counter on Metal, but no backend binds them
/// (<see cref="VulkanShaderLayout"/> rejects a shader that has one).</para>
/// </remarks>
internal static partial class ShaderUniformBlocks
{
    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex commentPattern();

    [GeneratedRegex(@"layout\s*\(([^)]*)\)\s*uniform\s+(\w+)\s*\{")]
    private static partial Regex declarationPattern();

    [GeneratedRegex(@"\bset\s*=\s*(\d+)")]
    private static partial Regex setPattern();

    [GeneratedRegex(@"\bbinding\s*=\s*(\d+)")]
    private static partial Regex bindingPattern();

    /// <summary>
    /// The uniform blocks that survived compiling, from each stage's GLSL source and optimised SPIR-V.
    /// </summary>
    /// <exception cref="InvalidOperationException">A block survived whose declaration couldn't be read from the source,
    /// so nothing could ever upload to it.</exception>
    public static ShaderUniformBlock[] Find(string vertexSource, byte[] vertexSpirv, string fragmentSource, byte[] fragmentSpirv)
    {
        var vertex = SpirvReflection.Parse(vertexSpirv).UniformBlocks;
        var fragment = SpirvReflection.Parse(fragmentSpirv).UniformBlocks;

        var names = new Dictionary<(uint Set, uint Binding), string>();
        readNames(vertexSource, names);
        readNames(fragmentSource, names);

        var bindings = vertex.Concat(fragment)
                             .Select(block => (block.Set, block.Binding))
                             .Distinct()
                             .OrderBy(binding => binding.Set)
                             .ThenBy(binding => binding.Binding)
                             .ToArray();

        var blocks = new ShaderUniformBlock[bindings.Length];

        for (int i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];

            if (!names.TryGetValue(binding, out string? name))
            {
                throw new InvalidOperationException(
                    $"The uniform block at set {binding.Set}, binding {binding.Binding} has no declaration that can be read: "
                    + "write it as layout(set = 0, binding = 5, std140) uniform Name { ... }, with plain numbers.");
            }

            blocks[i] = new ShaderUniformBlock(
                name,
                vertex.Any(block => block.Set == binding.Set && block.Binding == binding.Binding),
                fragment.Any(block => block.Set == binding.Set && block.Binding == binding.Binding),
                i);
        }

        return blocks;
    }

    private static void readNames(string source, Dictionary<(uint Set, uint Binding), string> names)
    {
        foreach (Match declaration in declarationPattern().Matches(commentPattern().Replace(source, " ")))
        {
            string qualifiers = declaration.Groups[1].Value;
            Match set = setPattern().Match(qualifiers);
            Match binding = bindingPattern().Match(qualifiers);

            if (binding.Success)
                names[(set.Success ? uint.Parse(set.Groups[1].Value) : 0, uint.Parse(binding.Groups[1].Value))] = declaration.Groups[2].Value;
        }
    }

    /// <summary>
    /// The blocks as text, for the shader cache.
    /// </summary>
    public static string Serialize(IEnumerable<ShaderUniformBlock> blocks)
    {
        var text = new StringBuilder();

        foreach (ShaderUniformBlock block in blocks)
            text.Append(block.Name).Append(' ').Append(block.InVertex ? 1 : 0).Append(' ').Append(block.InFragment ? 1 : 0).Append(' ').Append(block.Index).Append('\n');

        return text.ToString();
    }

    /// <summary>
    /// What <see cref="Serialize"/> wrote; false if it isn't that (a damaged cache entry), for compiling again.
    /// </summary>
    public static bool TryDeserialize(string text, out ShaderUniformBlock[] blocks)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        blocks = new ShaderUniformBlock[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            string[] fields = lines[i].Split(' ');

            if (fields.Length != 4 || !int.TryParse(fields[3], out int index))
                return false;

            blocks[i] = new ShaderUniformBlock(fields[0], fields[1] == "1", fields[2] == "1", index);
        }

        return true;
    }
}
