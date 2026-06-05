// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NativeSpecializationConstant
    {
        public uint ID;
        public ulong Constant;
    }
}
