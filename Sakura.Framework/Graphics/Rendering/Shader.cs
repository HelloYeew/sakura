// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Represents a shader program used in rendering.
/// </summary>
public class Shader : IShader
{
    private readonly GL gl;
    private readonly uint handle;
    private bool disposed;

    /// <summary>
    /// Creates a shader from embedded resources in the framework's own assembly.
    /// Used internally by the framework (e.g. the main scene shader, video shader).
    /// </summary>
    /// <param name="gl">The OpenGL context.</param>
    /// <param name="vertexResourcePath">Embedded resource path relative to the framework assembly root (e.g. "Resources/Shaders/shader.vert").</param>
    /// <param name="fragmentResourcePath">Embedded resource path relative to the framework assembly root.</param>
    public Shader(GL gl, string vertexResourcePath, string fragmentResourcePath)
        : this(gl, vertexResourcePath, fragmentResourcePath, typeof(Shader).Assembly)
    {
    }

    /// <summary>
    /// Creates a shader from embedded resources in the specified assembly.
    /// Use this overload to load shaders embedded in your game's own assembly.
    /// </summary>
    /// <param name="gl">The OpenGL context.</param>
    /// <param name="vertexResourcePath">Embedded resource path relative to the assembly root (e.g. "MyGame.Shaders.custom.vert" or "Shaders/custom.vert").</param>
    /// <param name="fragmentResourcePath">Embedded resource path relative to the assembly root.</param>
    /// <param name="assembly">The assembly containing the embedded shader resources.</param>
    public Shader(GL gl, string vertexResourcePath, string fragmentResourcePath, Assembly assembly)
    {
        this.gl = gl;

        uint vertex   = loadShaderFromAssembly(ShaderType.VertexShader, vertexResourcePath, assembly);
        uint fragment = loadShaderFromAssembly(ShaderType.FragmentShader, fragmentResourcePath, assembly);

        handle = this.gl.CreateProgram();
        this.gl.AttachShader(handle, vertex);
        this.gl.AttachShader(handle, fragment);
        this.gl.LinkProgram(handle);
        this.gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new Exception($"Error linking shader program: {this.gl.GetProgramInfoLog(handle)}");

        this.gl.DetachShader(handle, vertex);
        this.gl.DetachShader(handle, fragment);
        this.gl.DeleteShader(vertex);
        this.gl.DeleteShader(fragment);

        GlobalStatistics.Get<int>("Graphics", "Loaded Shaders").Value++;
    }

    /// <summary>
    /// Creates a shader directly from GLSL source strings.
    /// Use this overload when you load shader source yourself (e.g. from a file, network, or generated at runtime).
    /// </summary>
    /// <param name="gl">The OpenGL context.</param>
    /// <param name="vertexSource">GLSL source code for the vertex shader.</param>
    /// <param name="fragmentSource">GLSL source code for the fragment shader.</param>
    /// <param name="sourceStrings">Sentinel parameter — pass <see cref="ShaderSource.FromString"/> to distinguish from the resource-path overload.</param>
    public Shader(GL gl, string vertexSource, string fragmentSource, ShaderSource sourceStrings)
    {
        this.gl = gl;

        uint vertex   = compileShader(ShaderType.VertexShader,   vertexSource);
        uint fragment = compileShader(ShaderType.FragmentShader, fragmentSource);

        handle = this.gl.CreateProgram();
        this.gl.AttachShader(handle, vertex);
        this.gl.AttachShader(handle, fragment);
        this.gl.LinkProgram(handle);
        this.gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new Exception($"Error linking shader program: {this.gl.GetProgramInfoLog(handle)}");

        this.gl.DetachShader(handle, vertex);
        this.gl.DetachShader(handle, fragment);
        this.gl.DeleteShader(vertex);
        this.gl.DeleteShader(fragment);

        GlobalStatistics.Get<int>("Graphics", "Loaded Shaders").Value++;
    }

    /// <summary>
    /// Creates a shader by loading GLSL source from a <see cref="Platform.Storage"/>.
    /// Lets you store shaders in your game's data folder (e.g. Application Support) and
    /// hot-reload them without recompiling.
    /// </summary>
    public static Shader FromStorage(GL gl, Platform.Storage storage, string vertexPath, string fragmentPath)
    {
        string vertexSource = readStreamToString(storage.GetStream(vertexPath));
        string fragSource = readStreamToString(storage.GetStream(fragmentPath));
        return new Shader(gl, vertexSource, fragSource, ShaderSource.FromString);
    }

    private static string readStreamToString(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public uint Handle => handle;

    /// <summary>
    /// Uses the shader program for rendering.
    /// </summary>
    public void Use()
    {
        gl.UseProgram(handle);
        GlobalStatistics.Get<int>("Renderer", "Shader Binds").Value++;
    }

    /// <summary>
    /// Sets an integer uniform variable in the shader program.
    /// </summary>
    /// <param name="name">Name of the uniform variable.</param>
    /// <param name="value">Integer value to set.</param>
    public void SetUniform(string name, int value)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1) gl.Uniform1(location, value);
    }

    /// <summary>
    /// Sets a float uniform variable in the shader program.
    /// </summary>
    /// <param name="name">Name of the uniform variable.</param>
    /// <param name="value">Float value to set.</param>
    public void SetUniform(string name, float value)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1) gl.Uniform1(location, value);
    }

    /// <summary>
    /// Sets a boolean uniform variable in the shader program.
    /// </summary>
    /// <param name="name">Name of the uniform variable.</param>
    /// <param name="value">Boolean value to set.</param>
    public void SetUniform(string name, bool value)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1) gl.Uniform1(location, value ? 1 : 0);
    }

    /// <summary>
    /// Sets a <see cref="Matrix4x4"/> uniform variable in the shader program.
    /// </summary>
    /// <param name="name">Name of the uniform variable.</param>
    /// <param name="value">Matrix4x4 value to set.</param>
    public void SetUniform(string name, Matrix4x4 value)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1)
        {
            unsafe
            {
                gl.UniformMatrix4(location, 1, false, (float*)&value);
            }
        }
    }

    /// <summary>
    /// Sets a <see cref="Vector2"/> uniform variable in the shader program.
    /// </summary>
    /// <param name="name">Name of the uniform variable.</param>
    /// <param name="value">Vector2 value to set.</param>
    public void SetUniform(string name, Vector2 value)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1)
        {
            gl.Uniform2(location, value.X, value.Y);
        }
    }

    /// <summary>
    /// Sets a <see cref="Vector4"/> uniform variable in the shader program.
    /// </summary>
    /// <param name="name">Name of the uniform variable.</param>
    /// <param name="value">Vector4 value to set.</param>
    public void SetUniform(string name, Vector4 value)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1)
        {
            gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    /// <summary>
    /// Sets a <see cref="Color"/> uniform variable in the shader program.
    /// </summary>
    /// <param name="name">Name of the uniform variable.</param>
    /// <param name="value">Color value to set.</param>
    public void SetUniform(string name, Color value)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1)
        {
            gl.Uniform4(location, value.R / 255.0f, value.G / 255.0f, value.B / 255.0f, value.A / 255.0f);
        }
    }

    /// <summary>
    /// Sets an integer array uniform variable in the shader program (used for Sampler Arrays).
    /// </summary>
    public void SetUniformIntArray(string name, int[] values)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location != -1)
        {
            unsafe
            {
                fixed (int* ptr = values)
                {
                    gl.Uniform1(location, (uint)values.Length, ptr);
                }
            }
        }
    }

    /// <summary>
    /// Sets a 3×3 matrix uniform from a row-major float[9] array.
    /// Used by the video shader for YUV→RGB colour conversion coefficients.
    /// </summary>
    public void SetUniform(string name, float[] mat3x3)
    {
        int location = gl.GetUniformLocation(handle, name);
        if (location == -1) return;

        unsafe
        {
            fixed (float* p = mat3x3)
                gl.UniformMatrix3(location, 1, false, p);
        }
    }

    /// <summary>
    /// Loads and compiles a shader from an embedded resource.
    /// </summary>
    /// <param name="type">Type of the shader (OpenGL enum).</param>
    /// <param name="resourcePath">Path to the embedded resource.</param>
    /// <returns>Handle to the compiled shader.</returns>
    /// <exception cref="Exception">If there is an error compiling the shader.</exception>
    private uint loadShaderFromAssembly(ShaderType type, string resourcePath, Assembly assembly)
    {
        string src = readEmbeddedShader(resourcePath, assembly);
        src = resolveIncludes(src, resourcePath, assembly); // normalises line endings internally
        return compileShader(type, src, resourcePath);
    }

    private uint compileShader(ShaderType type, string src, string label = "<source>")
    {
        // GLSL source must be ASCII. Strip any non-ASCII characters that may appear
        // in comments (e.g. Unicode dashes, smart quotes) to prevent driver parse failures.
        if (src.Any(c => c > 127))
            src = new string(src.Select(c => c > 127 ? ' ' : c).ToArray());

        uint shaderHandle = gl.CreateShader(type);
        gl.ShaderSource(shaderHandle, src);
        gl.CompileShader(shaderHandle);

        string infoLog = gl.GetShaderInfoLog(shaderHandle);
        if (!string.IsNullOrWhiteSpace(infoLog))
            throw new Exception($"Error compiling shader of type {type} ({label}): {infoLog}");

        return shaderHandle;
    }

    /// <summary>
    /// Reads a single shader source file from embedded resources.
    /// </summary>
    private string readEmbeddedShader(string resourcePath, Assembly assembly)
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

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Resolves <c>#include "filename"</c> directives in shader source.
    /// Included files are loaded from the same directory as the parent shader
    /// within the same assembly. Includes are processed recursively.
    /// Non-ASCII source is safe — GLSL source must be ASCII, but we preserve
    /// the encoding as-is since the GLSL compiler handles byte strings.
    /// </summary>
    private string resolveIncludes(string src, string parentPath, Assembly assembly)
    {
        string directory = parentPath.Contains('/')
            ? parentPath[..(parentPath.LastIndexOf('/') + 1)]
            : string.Empty;

        // Normalise to Unix line endings so Split works correctly regardless of
        // how the embedded resource was stored (CRLF on Windows, LF on macOS/Linux).
        src = src.Replace("\r\n", "\n").Replace("\r", "\n");

        var result = new System.Text.StringBuilder();
        string[] lines = src.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.StartsWith("#include", StringComparison.Ordinal))
            {
                int start = trimmed.IndexOf('"');
                int end   = trimmed.LastIndexOf('"');

                if (start >= 0 && end > start)
                {
                    string includeName = trimmed[(start + 1)..end];
                    string includePath = directory + includeName;

                    string includeSource = readEmbeddedShader(includePath, assembly);
                    includeSource = resolveIncludes(includeSource, includePath, assembly);

                    // Trim trailing newlines from the included source before splicing
                    includeSource = includeSource.TrimEnd('\n', '\r');

                    result.Append("// ---- begin include: ").Append(includeName).Append(" ----\n");
                    result.Append(includeSource).Append('\n');
                    result.Append("// ---- end include: ").Append(includeName).Append(" ----\n");
                    continue;
                }
            }

            result.Append(lines[i]).Append('\n');
        }

        return result.ToString();
    }

    public void Dispose()
    {
        if (disposed) return;
        gl.DeleteProgram(handle);
        GlobalStatistics.Get<int>("Graphics", "Loaded Shaders").Value--;
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
