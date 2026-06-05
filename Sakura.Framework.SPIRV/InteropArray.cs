// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct InteropArray
    {
        public uint Count;
        public void* Data;

        public InteropArray(uint count, void* data)
        {
            Count = count;
            Data = data;
        }

        public ref T Ref<T>(int index)
        {
            if (index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref Unsafe.AsRef<T>((byte*)Data + (index * Unsafe.SizeOf<T>()));
        }

        public ref T Ref<T>(uint index)
        {
            if (index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref Unsafe.AsRef<T>((byte*)Data + (index * Unsafe.SizeOf<T>()));
        }
    }
}
