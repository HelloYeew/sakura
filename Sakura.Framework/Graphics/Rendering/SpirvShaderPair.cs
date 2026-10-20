// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A vertex + fragment shader pair compiled to SPIR-V.
/// </summary>
/// <remarks>
/// <see cref="Vertex"/> and <see cref="Fragment"/> are the modules the driver runs. When they are
/// optimised, the optimiser strips every name from them, so they no longer say which block or texture a
/// binding is. The reflection modules are compiled from the same source with debug information instead:
/// they carry the same set, binding, location and offset decorations, which the source declares
/// explicitly, plus the names. When the pair was compiled for debugging, both pairs are the same arrays.
/// </remarks>
public sealed record SpirvShaderPair(byte[] Vertex, byte[] Fragment, byte[] VertexForReflection, byte[] FragmentForReflection);
