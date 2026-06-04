// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Backend-agnostic shader program abstraction.
/// </summary>
public interface IShader : IDisposable
{
    /// <summary>
    /// Activates this shader program for subsequent draw calls.
    /// </summary>
    void Use();

    void SetUniform(string name, int value);
    void SetUniform(string name, float value);
    void SetUniform(string name, bool value);
    void SetUniform(string name, Matrix4x4 value);
    void SetUniform(string name, Vector2 value);
    void SetUniform(string name, Vector4 value);
    void SetUniform(string name, Color value);
    void SetUniformIntArray(string name, int[] values);

    /// <summary>
    /// Sets a 3×3 matrix uniform from a row-major float[9] array.
    /// Used by the video shader for YUV→RGB colour conversion coefficients.
    /// </summary>
    void SetUniform(string name, float[] mat3X3);

    /// <summary>
    /// The native program handle. Used by backend-specific code that needs raw access.
    /// </summary>
    uint Handle { get; }
}
