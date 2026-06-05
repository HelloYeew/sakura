// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    /// <summary>
    /// A specialization constant value to substitute into a SPIR-V shader during cross-compilation.
    /// Layout must match the native <c>NativeSpecializationConstant</c> struct exactly (Pack=1).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SpecializationConstant
    {
        /// <summary>The specialization constant ID in the SPIR-V shader.</summary>
        public uint ID;

        /// <summary>Raw 64-bit storage for the constant value.</summary>
        public ulong Data;

        public static SpecializationConstant Create(uint id, bool value)  => new SpecializationConstant { ID = id, Data = value ? 1u : 0u };
        public static SpecializationConstant Create(uint id, int value)   => new SpecializationConstant { ID = id, Data = (ulong)(uint)value };
        public static SpecializationConstant Create(uint id, uint value)  => new SpecializationConstant { ID = id, Data = value };
        public static SpecializationConstant Create(uint id, float value) => new SpecializationConstant { ID = id, Data = BitConverter.SingleToUInt32Bits(value) };
    }

    /// <summary>
    /// Controls the parameters of shader cross-compilation from SPIR-V to a target language.
    /// </summary>
    public class CrossCompileOptions
    {
        /// <summary>
        /// If true, includes a clip-space Z-range fixup at the end of the vertex shader.
        /// </summary>
        public bool FixClipSpaceZ { get; set; }

        /// <summary>
        /// If true, inverts the clip-space Y value at the end of the vertex shader.
        /// </summary>
        public bool InvertVertexOutputY { get; set; }

        /// <summary>
        /// If true, forces all resource names into a normalized form (matters for GLSL targets).
        /// </summary>
        public bool NormalizeResourceNames { get; set; }

        /// <summary>
        /// Specialization constants to substitute during compilation, matched by ID.
        /// </summary>
        public SpecializationConstant[] Specializations { get; set; }

        public CrossCompileOptions()
        {
            Specializations = Array.Empty<SpecializationConstant>();
        }

        public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY)
        {
            FixClipSpaceZ = fixClipSpaceZ;
            InvertVertexOutputY = invertVertexOutputY;
            Specializations = Array.Empty<SpecializationConstant>();
        }

        public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY, bool normalizeResourceNames)
        {
            FixClipSpaceZ = fixClipSpaceZ;
            InvertVertexOutputY = invertVertexOutputY;
            NormalizeResourceNames = normalizeResourceNames;
            Specializations = Array.Empty<SpecializationConstant>();
        }

        public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY, params SpecializationConstant[] specializations)
        {
            FixClipSpaceZ = fixClipSpaceZ;
            InvertVertexOutputY = invertVertexOutputY;
            Specializations = specializations;
        }

        public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY, bool normalizeResourceNames, params SpecializationConstant[] specializations)
        {
            FixClipSpaceZ = fixClipSpaceZ;
            InvertVertexOutputY = invertVertexOutputY;
            NormalizeResourceNames = normalizeResourceNames;
            Specializations = specializations;
        }
    }
}
