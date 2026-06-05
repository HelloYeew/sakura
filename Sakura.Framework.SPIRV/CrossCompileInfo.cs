// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct CrossCompileInfo
    {
        public CrossCompileTarget Target;
        public Bool32 FixClipSpaceZ;
        public Bool32 InvertY;
        public Bool32 NormalizeResourceNames;
        public InteropArray Specializations;
        public InteropArray VertexShader;
        public InteropArray FragmentShader;
        public InteropArray ComputeShader;
    }
}
