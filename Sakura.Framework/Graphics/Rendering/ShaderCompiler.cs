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

    [GeneratedRegex(@"^[ \t]*#[ \t]*version\b.*$", RegexOptions.Multiline)]
    private static partial Regex versionPatternRegex();

    private static readonly Regex version_pattern = versionPatternRegex();

    /// <summary>
    /// Defined in every fragment shader cross-compiled to HLSL. Direct3D's shader compiler moves a texture
    /// sample that takes its derivatives implicitly out of any branch that can differ between
    /// neighbouring pixels, so a shader that picks its texture per pixel samples every candidate.
    /// Shaders that sample that way pass explicit derivatives instead when this is defined.
    /// </summary>
    private const string explicit_gradients_define = "SAKURA_EXPLICIT_GRADIENTS";

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
        // Before the key, which then tells the HLSL variant apart from the other targets' on its own.
        if (target == CrossCompileTarget.HLSL)
            fragmentSource = defineAfterVersion(fragmentSource, explicit_gradients_define);

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
    /// Reads a vertex + fragment shader pair from <paramref name="sourceStorage"/> (resolving
    /// <c>#include</c> directives) and compiles each stage to SPIR-V for Vulkan. Nothing is
    /// cross-compiled, so the shaders' explicit set/binding/location decorations reach the driver as
    /// written. The result is cached in <paramref name="cache"/> when one is supplied.
    /// </summary>
    /// <param name="sourceStorage">Storage containing the GLSL 450 source files.</param>
    /// <param name="vertexPath">Path within the storage to the vertex shader (e.g. "shader.vert").</param>
    /// <param name="fragmentPath">Path within the storage to the fragment shader (e.g. "shader.frag").</param>
    /// <param name="cache">Optional disk cache. When null, compilation always runs.</param>
    /// <param name="debug">
    /// Emit source-level debug information (for RenderDoc and similar) instead of optimising.
    /// </param>
    public static SpirvShaderPair GetOrCompileSpirv(
        Storage sourceStorage,
        string vertexPath,
        string fragmentPath,
        DiskCache cache = null,
        bool debug = false)
    {
        string vertSrc = ReadWithIncludes(sourceStorage, vertexPath);
        string fragSrc = ReadWithIncludes(sourceStorage, fragmentPath);

        return GetOrCompileSpirvSource(vertSrc, fragSrc, cache, debug, vertexPath, fragmentPath);
    }

    /// <summary>
    /// Compiles already-resolved GLSL 450 source strings to SPIR-V, using <paramref name="cache"/> when
    /// supplied. <paramref name="vertexName"/> and <paramref name="fragmentName"/> only label logs and
    /// compiler errors.
    /// </summary>
    public static SpirvShaderPair GetOrCompileSpirvSource(
        string vertexSource,
        string fragmentSource,
        DiskCache cache = null,
        bool debug = false,
        string vertexName = null,
        string fragmentName = null)
    {
        string key = $"spirv#{DiskCache.HashString(vertexSource)}" +
                     $"#{DiskCache.HashString(fragmentSource)}" +
                     $"#{(debug ? 1 : 0)}#{cache_version}";

        string label = vertexName != null || fragmentName != null ? $"{vertexName}+{fragmentName}" : key[6..18];

        if (cache != null && cache.TryRead(key, out byte[] cached) && tryUnpackSpirv(cached, out var cachedPair))
        {
            Logger.Verbose($"☀️ Shader cache hit for {label} → SPIR-V, loaded from cache");
            return cachedPair;
        }

        Logger.Verbose($"☀️ Shader cache miss for {label} → SPIR-V, compiling");

        var debugOptions = new GlslCompileOptions(true);

        // Named modules for reflection. Compiling with debug information is the only mode that keeps the
        // names, and the optimiser removes all of them.
        byte[] namedVert = SpirvCompilation.CompileGlslToSpirv(vertexSource, vertexName, ShaderStages.Vertex, debugOptions).SpirvBytes;
        byte[] namedFrag = SpirvCompilation.CompileGlslToSpirv(fragmentSource, fragmentName, ShaderStages.Fragment, debugOptions).SpirvBytes;

        var pair = debug
            ? new SpirvShaderPair(namedVert, namedFrag, namedVert, namedFrag)
            : new SpirvShaderPair(
                SpirvCompilation.CompileGlslToSpirv(vertexSource, vertexName, ShaderStages.Vertex, GlslCompileOptions.Default).SpirvBytes,
                SpirvCompilation.CompileGlslToSpirv(fragmentSource, fragmentName, ShaderStages.Fragment, GlslCompileOptions.Default).SpirvBytes,
                namedVert,
                namedFrag);

        if (cache != null)
        {
            cache.Write(key, packSpirv(pair));
            Logger.Verbose($"☀️ Shader compiled for {label} -> SPIR-V!");
        }

        return pair;
    }

    /// <summary>
    /// Adds <c>#define <paramref name="name"/></c> on the line after the <c>#version</c> directive, the
    /// earliest point GLSL allows. Source without a <c>#version</c> line is returned unchanged.
    /// </summary>
    private static string defineAfterVersion(string source, string name)
    {
        Match version = version_pattern.Match(source);

        if (!version.Success)
            return source;

        int end = version.Index + version.Length;
        return $"{source[..end]}\n#define {name}{source[end..]}";
    }

    private static byte[] packSpirv(SpirvShaderPair pair)
    {
        using var ms = new MemoryStream();

        using (var writer = new BinaryWriter(ms))
        {
            foreach (byte[] module in new[] { pair.Vertex, pair.Fragment, pair.VertexForReflection, pair.FragmentForReflection })
            {
                writer.Write(module.Length);
                writer.Write(module);
            }
        }

        return ms.ToArray();
    }

    private static bool tryUnpackSpirv(byte[] data, out SpirvShaderPair pair)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte[][] modules = new byte[4][];

            for (int i = 0; i < modules.Length; i++)
            {
                modules[i] = reader.ReadBytes(reader.ReadInt32());

                // A short read means the entry is damaged or was written by something else; recompile
                // rather than hand a truncated module to the driver.
                if (modules[i].Length == 0 || modules[i].Length % 4 != 0)
                {
                    pair = null;
                    return false;
                }
            }

            pair = new SpirvShaderPair(modules[0], modules[1], modules[2], modules[3]);
            return ms.Position == ms.Length;
        }
        catch
        {
            pair = null;
            return false;
        }
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
