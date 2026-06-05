// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.SPIRV
{
    /// <summary>
    /// The output of a cross-compile operation of a vertex and fragment shader from SPIR-V to some target language.
    /// </summary>
    public class VertexFragmentCompilationResult
    {
        /// <summary>The translated vertex shader source code.</summary>
        public string VertexShader { get; }

        /// <summary>The translated fragment shader source code.</summary>
        public string FragmentShader { get; }

        internal VertexFragmentCompilationResult(string vertexCode, string fragmentCode)
        {
            VertexShader = vertexCode;
            FragmentShader = fragmentCode;
        }
    }
}
