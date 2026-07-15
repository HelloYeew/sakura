// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.SPIRV;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Shared utility that compile shader to target shader language
/// </summary>
public static partial class ShaderCompiler
{
    /// <summary>
    /// Bump this when the compiler output format or options change to invalidate all existing
    /// cache entries without having to clear the cache directory by hand.
    /// </summary>
    private const int cache_version = 1;

    [GeneratedRegex(@"^\s*#\s*include\s+[""<](.*)["">]", RegexOptions.Compiled)]
    private static partial Regex includePatternRegex();

    private static readonly Regex include_pattern = includePatternRegex();

    /// <summary>
    /// Reads a vertex + fragment shader pair from <paramref name="sourceStorage"/> (resolving
    /// <c>#include</c> directives), cross-compiles them to <paramref name="target"/>, and returns
    /// the resulting source. The result is cached in <paramref name="cache"/> when one is supplied.
    /// </summary>
    /// <param name="sourceStorage">Storage containing the GLSL 450 source files.</param>
    /// <param name="vertexPath">Path within the storage to the vertex shader (e.g. "shader.vert").</param>
    /// <param name="fragmentPath">Path within the storage to the fragment shader (e.g. "shader.frag").</param>
    /// <param name="target">The shading language to produce.</param>
    /// <param name="cache">Optional disk cache. When null, compilation always runs.</param>
    public static (string vert, string frag) GetOrCompile(
        Storage sourceStorage,
        string vertexPath,
        string fragmentPath,
        CrossCompileTarget target,
        DiskCache cache = null)
    {
        string vertSrc = ReadWithIncludes(sourceStorage, vertexPath);
        string fragSrc = ReadWithIncludes(sourceStorage, fragmentPath);

        // readable name for logs, e.g. "shader.vert+shader.frag".
        return GetOrCompileSource(vertSrc, fragSrc, target, cache, $"{vertexPath}+{fragmentPath}");
    }

    /// <summary>
    /// Cross-compiles already-resolved GLSL 450 source strings to <paramref name="target"/>,
    /// using <paramref name="cache"/> when supplied. Useful when the source is generated at runtime
    /// rather than loaded from storage.
    /// </summary>
    public static (string vert, string frag) GetOrCompileSource(
        string vertexSource,
        string fragmentSource,
        CrossCompileTarget target,
        DiskCache cache = null,
        string name = null)
    {
        string key = $"{DiskCache.HashString(vertexSource)}" +
                     $"#{DiskCache.HashString(fragmentSource)}" +
                     $"#{(int)target}#{cache_version}";

        string label = !string.IsNullOrEmpty(name) ? name : (key.Length > 12 ? key[..12] : key);

        if (cache != null && cache.TryReadStrings(key, out string cachedVert, out string cachedFrag))
        {
            Logger.Verbose($"☀️ Shader cache hit for {label} → {target}, loaded from cache");
            return (cachedVert, cachedFrag);
        }

        Logger.Verbose($"☀️ Shader cache miss for {label} → {target}, compiling");

        var options = new CrossCompileOptions(
            // D3D clip space is [0,1] on Z (vs OpenGL/Metal's [-1,1]), so the depth range must be
            // remapped for HLSL only.
            fixClipSpaceZ: target == CrossCompileTarget.HLSL,
            // Metal and Direct3D both use a top-left framebuffer origin (vs OpenGL's bottom-left),
            // so flip Y for MSL and HLSL.
            invertVertexOutputY: target == CrossCompileTarget.MSL || target == CrossCompileTarget.HLSL
        );

        VertexFragmentCompilationResult result = SpirvCompilation.CompileVertexFragment(
            Encoding.UTF8.GetBytes(vertexSource),
            Encoding.UTF8.GetBytes(fragmentSource),
            target,
            options);

        if (cache != null)
        {
            cache.WriteStrings(key, result.VertexShader, result.FragmentShader);
            Logger.Verbose($"☀️ Shader compiled for {label} -> {target}!");
        }

        return (result.VertexShader, result.FragmentShader);
    }

    /// <summary>
    /// Reads shader source from a <see cref="Storage"/>, recursively resolving <c>#include</c>
    /// directives relative to the same storage. Mirrors the include handling in <c>GLShader</c>
    /// so a shader compiles identically whether loaded directly or through this compiler.
    /// </summary>
    public static string ReadWithIncludes(Storage storage, string path)
    {
        using Stream stream = storage.GetStream(path, FileAccess.Read, FileMode.Open);
        string src = readStream(stream);

        return ResolveIncludes(src, name =>
        {
            try
            {
                using Stream s = storage.GetStream(name, FileAccess.Read, FileMode.Open);
                return readStream(s);
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException(
                    $"Shader #include '{name}' not found in storage '{storage.GetFullPath(string.Empty)}'.", ex);
            }
        });
    }

    /// <summary>
    /// Resolves <c>#include</c> directives by passing each include name to <paramref name="loadInclude"/>.
    /// Uses a compiled regex instead of manual string parsing — handles both <c>"file"</c> and
    /// <c>&lt;file&gt;</c> syntax, and tolerates whitespace between <c>#</c> and <c>include</c>.
    /// </summary>
    public static string ResolveIncludes(string src, Func<string, string> loadInclude)
    {
        src = src.Replace("\r\n", "\n").Replace("\r", "\n");

        var result = new StringBuilder();

        foreach (string line in src.Split('\n'))
        {
            Match match = include_pattern.Match(line);

            if (match.Success)
            {
                string includeName = match.Groups[1].Value.Trim();
                string includeSource = loadInclude(includeName);

                // Recurse so included files can themselves contain #include.
                includeSource = ResolveIncludes(includeSource, loadInclude);
                includeSource = includeSource.TrimEnd('\n', '\r');

                result.Append("// ---- begin include: ").Append(includeName).Append(" ----\n");
                result.Append(includeSource).Append('\n');
                result.Append("// ---- end include: ").Append(includeName).Append(" ----\n");
            }
            else
            {
                result.Append(line).Append('\n');
            }
        }

        return result.ToString();
    }

    private static string readStream(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
