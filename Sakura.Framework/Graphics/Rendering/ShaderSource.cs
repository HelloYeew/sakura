// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Sentinel value used to disambiguate <see cref="Shader"/> constructor overloads.
/// Pass <see cref="FromString"/> when providing raw GLSL source code directly,
/// as opposed to embedded resource paths.
/// </summary>
/// <example>
/// <code>
/// // Load GLSL from a file you read yourself
/// string vert = File.ReadAllText("Shaders/my.vert");
/// string frag = File.ReadAllText("Shaders/my.frag");
/// var shader = new Shader(gl, vert, frag, ShaderSource.FromString);
///
/// // Or use the Storage helper
/// var shader = Shader.FromStorage(gl, storage, "Shaders/my.vert", "Shaders/my.frag");
/// </code>
/// </example>
public enum ShaderSource
{
    FromString
}
