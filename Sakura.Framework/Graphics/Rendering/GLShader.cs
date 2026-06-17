// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Represents a shader program used in rendering.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class GLShader : IShader
{
    private static readonly GlobalStatistic<int> stat_shader_binds = GlobalStatistics.Get<int>("Renderer", "Shader Binds");

    private readonly GL gl;
    private readonly uint handle;
    private bool disposed;

    // glGetUniformLocation is a string lookup inside the driver; caching locations keeps
    // per-frame SetUniform calls (projection, masking state, ...) cheap.
    private readonly Dictionary<string, int> uniformLocations = new Dictionary<string, int>();

    private int getUniformLocation(string name)
    {
        if (!uniformLocations.TryGetValue(name, out int location))
        {
            location = gl.GetUniformLocation(handle, name);
            uniformLocations[name] = location;
        }

        return location;
    }

    /// <summary>
    /// Creates a shader from a specified <see cref="Storage"/>
    /// </summary>
    /// <param name="gl">The OpenGL context.</param>
    /// <param name="storage">Storage containing the shader files.</param>
    /// <param name="vertexPath">Path inside the storage to the vertex shader (e.g. "shader.vert").</param>
    /// <param name="fragmentPath">Path inside the storage to the fragment shader (e.g. "shader.frag").</param>
    public GLShader(GL gl, Storage storage, string vertexPath, string fragmentPath)
    {
        this.gl = gl;

        string vertSrc = loadFromStorage(storage, vertexPath);
        string fragSrc = loadFromStorage(storage, fragmentPath);

        uint vertex = compileShader(ShaderType.VertexShader,   vertSrc,  vertexPath);
        uint fragment = compileShader(ShaderType.FragmentShader, fragSrc, fragmentPath);

        handle = link(vertex, fragment);
        GlobalStatistics.Get<int>("Graphics", "Loaded Shaders").Value++;
    }

    /// <summary>
    /// Compiles a shader directly from GLSL source strings.
    /// Use this when generating shader source at runtime.
    /// </summary>
    /// <param name="gl">The OpenGL context.</param>
    /// <param name="vertexSource">GLSL vertex shader source.</param>
    /// <param name="fragmentSource">GLSL fragment shader source.</param>
    public GLShader(GL gl, string vertexSource, string fragmentSource)
    {
        this.gl = gl;

        uint vertex   = compileShader(ShaderType.VertexShader,   vertexSource);
        uint fragment = compileShader(ShaderType.FragmentShader, fragmentSource);

        handle = link(vertex, fragment);
        GlobalStatistics.Get<int>("Graphics", "Loaded Shaders").Value++;
    }

    /// <summary>
    /// Loads a shader from embedded resources in a specific assembly.
    /// Intended for framework-internal use (main scene shader, video shader).
    /// Prefer the Storage-based constructor for all new code.
    /// </summary>
    internal GLShader(GL gl, string vertexResourcePath, string fragmentResourcePath, Assembly assembly)
    {
        this.gl = gl;

        uint vertex   = loadFromAssembly(ShaderType.VertexShader,   vertexResourcePath, assembly);
        uint fragment = loadFromAssembly(ShaderType.FragmentShader, fragmentResourcePath, assembly);

        handle = link(vertex, fragment);
        GlobalStatistics.Get<int>("Graphics", "Loaded Shaders").Value++;
    }

    public nint Handle => (nint)handle;

    public void Use()
    {
        gl.UseProgram(handle);
        stat_shader_binds.Value++;
    }

    public void SetUniform(string name, int value)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            gl.Uniform1(loc, value);
        }
    }

    public void SetUniform(string name, float value)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            gl.Uniform1(loc, value);
        }
    }

    public void SetUniform(string name, bool value)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            gl.Uniform1(loc, value ? 1 : 0);
        }
    }

    public void SetUniform(string name, Matrix4x4 value)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            unsafe
            {
                Matrix4x4 local = value;
                gl.UniformMatrix4(loc, 1, false, (float*)&local);
            }
        }
    }

    public void SetUniform(string name, Vector2 value)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            gl.Uniform2(loc, value.X, value.Y);
        }
    }

    public void SetUniform(string name, Vector4 value)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            gl.Uniform4(loc, value.X, value.Y, value.Z, value.W);
        }
    }

    public void SetUniform(string name, Color value)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            gl.Uniform4(loc, value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
        }
    }

    public void SetUniformIntArray(string name, int[] values)
    {
        int loc = getUniformLocation(name);
        if (loc != -1)
        {
            unsafe { fixed (int* p = values) gl.Uniform1(loc, (uint)values.Length, p); }
        }
    }

    public void SetUniform(string name, float[] mat3x3)
    {
        int loc = getUniformLocation(name);
        if (loc == -1) return;
        unsafe { fixed (float* p = mat3x3) gl.UniformMatrix3(loc, 1, false, p); }
    }

    public void Dispose()
    {
        if (disposed) return;
        gl.DeleteProgram(handle);
        GlobalStatistics.Get<int>("Graphics", "Loaded Shaders").Value--;
        disposed = true;
        GC.SuppressFinalize(this);
    }

    [GeneratedRegex(@"^\s*#\s*include\s+[""<](.*)[""&gt;]", RegexOptions.Compiled)]
    private static partial Regex IncludePatternRegex();

    /// <summary>
    /// Matches <c>#include "file"</c> or <c>#include &lt;file&gt;</c>, with optional
    /// whitespace between <c>#</c> and <c>include</c>. Capture group 1 is the filename.
    /// Compiled once as a static field — no per-call allocation.
    /// </summary>
    private static readonly Regex IncludePattern = IncludePatternRegex();

    /// <summary>
    /// Reads shader source from a <see cref="Storage"/>, resolving
    /// <c>#include</c> directives relative to the same storage.
    /// </summary>
    private static string loadFromStorage(Storage storage, string path)
    {
        using Stream stream = storage.GetStream(path, FileAccess.Read, FileMode.Open);
        string src = readStream(stream);
        return ShaderCompiler.ResolveIncludes(src, name =>
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

    private uint loadFromAssembly(ShaderType type, string resourcePath, Assembly assembly)
    {
        string directory = resourcePath.Contains('/')
            ? resourcePath[..(resourcePath.LastIndexOf('/') + 1)]
            : string.Empty;

        string src = readEmbeddedResource(resourcePath, assembly);
        src = ShaderCompiler.ResolveIncludes(src, includeName => readEmbeddedResource(directory + includeName, assembly));

        return compileShader(type, src, resourcePath);
    }

    private static string readEmbeddedResource(string resourcePath, Assembly assembly)
    {
        string resourceName = $"{assembly.GetName().Name}.{resourcePath.Replace('/', '.')}";

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Logger.Error($"Embedded shader resource not found: {resourceName}");
            Logger.Verbose("Available resources:");
            foreach (string name in assembly.GetManifestResourceNames())
                Logger.Verbose($"- {name}");
            throw new FileNotFoundException("Embedded shader resource not found.", resourceName);
        }

        return readStream(stream);
    }

    private uint compileShader(ShaderType type, string src, string label = "<source>")
    {
        // GLSL source must be ASCII — strip non-ASCII characters that may appear
        // in comments (Unicode dashes, smart quotes) to prevent driver failures.
        if (src.Any(c => c > 127))
            src = new string(src.Select(c => c > 127 ? ' ' : c).ToArray());

        uint shaderHandle = gl.CreateShader(type);
        gl.ShaderSource(shaderHandle, src);
        gl.CompileShader(shaderHandle);

        string infoLog = gl.GetShaderInfoLog(shaderHandle);
        if (!string.IsNullOrWhiteSpace(infoLog))
            throw new Exception($"Error compiling shader ({label}): {infoLog}");

        return shaderHandle;
    }

    private uint link(uint vertex, uint fragment)
    {
        uint prog = gl.CreateProgram();
        gl.AttachShader(prog, vertex);
        gl.AttachShader(prog, fragment);
        gl.LinkProgram(prog);
        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new Exception($"Error linking shader program: {gl.GetProgramInfoLog(prog)}");

        gl.DetachShader(prog, vertex);
        gl.DetachShader(prog, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        return prog;
    }

    private static string readStream(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
