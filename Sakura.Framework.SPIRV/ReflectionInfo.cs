// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

// Internal-only structs that mirror the native libsakura-spirv reflection layout.
// We do not surface reflection data to callers — these exist solely to maintain
// the correct binary layout of CompilationResult so the native interop is valid.

using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ReflectionInfo
    {
        public InteropArray VertexElements;   // InteropArray<NativeVertexElementDescription>
        public InteropArray ResourceLayouts;  // InteropArray<NativeResourceLayoutDescription>
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NativeVertexElementDescription
    {
        public InteropArray Name;     // InteropArray<byte>
        public uint Semantic;         // VertexElementSemantic (uint-sized)
        public uint Format;           // VertexElementFormat  (uint-sized)
        public uint Offset;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NativeResourceLayoutDescription
    {
        public InteropArray ResourceElements; // InteropArray<NativeResourceElementDescription>
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NativeResourceElementDescription
    {
        public InteropArray Name;    // InteropArray<byte>
        public uint Kind;            // ResourceKind            (uint-sized)
        public uint Stages;          // ShaderStages            (uint-sized)
        public uint Options;         // ResourceLayoutElementOptions (uint-sized)
    }
}
