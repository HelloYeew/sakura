// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.SPIRV
{
    /// <summary>
    /// The output of a cross-compile operation of a compute shader from SPIR-V to some target language.
    /// </summary>
    public class ComputeCompilationResult
    {
        /// <summary>The translated compute shader source code.</summary>
        public string ComputeShader { get; }

        internal ComputeCompilationResult(string computeCode)
        {
            ComputeShader = computeCode;
        }
    }
}
