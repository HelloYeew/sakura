// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct GlslCompileInfo
    {
        /// <summary>
        /// Element type: byte
        /// </summary>
        public InteropArray SourceText;
        /// <summary>
        /// Element type: byte
        /// </summary>
        public InteropArray FileName;
        public ShadercShaderKind Kind;
        public Bool32 Debug;
        /// <summary>
        /// Element type: NativeMacroDefinition
        /// </summary>
        public InteropArray Macros;
    };
}
