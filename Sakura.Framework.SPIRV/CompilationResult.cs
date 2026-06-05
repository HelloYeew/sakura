// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct CompilationResult
    {
        public Bool32 Succeeded;
        public InteropArray DataBuffers;
        public ReflectionInfo ReflectionInfo;

        public uint GetLength(uint index)
        {
            if (index >= DataBuffers.Count) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return DataBuffers.Ref<InteropArray>(index).Count;
        }

        public void* GetData(uint index)
        {
            if (index >= DataBuffers.Count) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return DataBuffers.Ref<InteropArray>(index).Data;
        }
    }
}
