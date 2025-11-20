// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Represents a shader program used in rendering.
/// </summary>
public class Shader : IDisposable
{
    private readonly GL gl;
    private readonly uint handle;
    private bool disposed;

    public Shader(GL gl, string vertexResourcePath, string fragmentResourcePath)
    {
        this.gl = gl;

        uint vertex = loadShader(ShaderType.VertexShader, vertexResourcePath);
        uint fragment = loadShader(ShaderType.FragmentShader, fragmentResourcePath);

        handle = this.gl.CreateProgram();
        this.gl.AttachShader(handle, vertex);
        this.gl.AttachShader(handle, fragment);
        this.gl.LinkProgram(handle);
        this.gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            throw new Exception($"Error linking shader program: {this.gl.GetProgramInfoLog(handle)}");
        }

        this.gl.DetachShader(handle, vertex);
        this.gl.DetachShader(handle, fragment);
        this.gl.DeleteShader(vertex);
        this.gl.DeleteShader(fragment);
    }

    public uint Handle => handle;

    /// <summary>
    /// Uses the shader program for rendering.
    /// </summary>
    public void Use()
    {
        gl.UseProgram(handle);
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
    /// Loads and compiles a shader from an embedded resource.
    /// </summary>
    /// <param name="type">Type of the shader (OpenGL enum).</param>
    /// <param name="resourcePath">Path to the embedded resource.</param>
    /// <returns>Handle to the compiled shader.</returns>
    /// <exception cref="Exception">If there is an error compiling the shader.</exception>
    private uint loadShader(ShaderType type, string resourcePath)
    {
        string src = readEmbededResource(resourcePath);
        uint handle = gl.CreateShader(type);
        gl.ShaderSource(handle, src);
        gl.CompileShader(handle);

        string infoLog = gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
        {
            throw new Exception($"Error compiling shader of type {type} with resource path {resourcePath}: {infoLog}");
        }

        return handle;
    }

    /// <summary>
    /// Reads an embedded resource file and returns its content as a string.
    /// </summary>
    /// <param name="resourcePath">Path to the embedded resource.</param>
    /// <returns>Content of the resource file as a string.</returns>
    /// <exception cref="FileNotFoundException">If the resource is not found.</exception>
    private string readEmbededResource(string resourcePath)
    {
        // TODO: The EmbeddedResourceStorage class should be replace this method.
        Assembly assembly = typeof(Shader).Assembly;
        string resourceName = $"{assembly.GetName().Name}.{resourcePath.Replace('/', '.')}";

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Logger.Error($"Embedded resource not found: {resourceName}");
            // Log all available resources for debugging
            Logger.Verbose("Available resources:");
            foreach (string name in assembly.GetManifestResourceNames())
                Logger.Verbose($"- {name}");
            throw new FileNotFoundException("Embedded resource not found.", resourceName);
        }

        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (disposed) return;
        gl.DeleteProgram(handle);
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
