// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

internal enum SpirvStage
{
    Vertex,
    Fragment,
    Other,
}

internal enum SpirvScalarKind
{
    Float,
    SignedInt,
    UnsignedInt,
    Bool,
    Other,
}

/// <summary>
/// The kind of a descriptor-backed resource that is neither a uniform block nor a combined image
/// sampler. Reflection reports these so a shader using one can be rejected by name, rather than
/// failing later inside the driver.
/// </summary>
internal enum SpirvOtherResourceKind
{
    StorageBuffer,
    Image,
    Sampler,
    Unknown,
}

internal readonly record struct SpirvBlockMember(string Name, uint Offset, uint Size);

internal sealed record SpirvUniformBlock(string Name, uint Set, uint Binding, uint Size, IReadOnlyList<SpirvBlockMember> Members);

internal sealed record SpirvSampler(string Name, uint Set, uint Binding, uint ArrayLength);

internal sealed record SpirvOtherResource(string Name, uint Set, uint Binding, SpirvOtherResourceKind Kind);

internal readonly record struct SpirvVertexInput(string Name, uint Location, SpirvScalarKind ComponentKind, uint ComponentCount);

/// <summary>
/// Reads the resource interface of one SPIR-V module: its uniform blocks, samplers and vertex inputs,
/// with the set, binding and location each one was decorated with.
/// </summary>
/// <remarks>
/// Only the instructions that describe the interface are decoded (names, decorations, types,
/// constants and variables); function bodies are skipped by word count. Names come from
/// <c>OpName</c>/<c>OpMemberName</c>, which the optimiser strips: an optimised module still reflects
/// every set, binding, location and offset, but with empty names, so name lookups are done on
/// <see cref="SpirvShaderPair.VertexForReflection"/> and its fragment counterpart.
/// </remarks>
internal sealed class SpirvReflection
{
    private const uint magic_number = 0x07230203;

    private const ushort op_name = 5;
    private const ushort op_member_name = 6;
    private const ushort op_entry_point = 15;
    private const ushort op_type_bool = 20;
    private const ushort op_type_int = 21;
    private const ushort op_type_float = 22;
    private const ushort op_type_vector = 23;
    private const ushort op_type_matrix = 24;
    private const ushort op_type_image = 25;
    private const ushort op_type_sampler = 26;
    private const ushort op_type_sampled_image = 27;
    private const ushort op_type_array = 28;
    private const ushort op_type_runtime_array = 29;
    private const ushort op_type_struct = 30;
    private const ushort op_type_pointer = 32;
    private const ushort op_constant = 43;
    private const ushort op_spec_constant = 50;
    private const ushort op_variable = 59;
    private const ushort op_decorate = 71;
    private const ushort op_member_decorate = 72;

    private const uint decoration_block = 2;
    private const uint decoration_buffer_block = 3;
    private const uint decoration_array_stride = 6;
    private const uint decoration_matrix_stride = 7;
    private const uint decoration_built_in = 11;
    private const uint decoration_location = 30;
    private const uint decoration_binding = 33;
    private const uint decoration_descriptor_set = 34;
    private const uint decoration_offset = 35;

    private const uint storage_uniform_constant = 0;
    private const uint storage_input = 1;
    private const uint storage_uniform = 2;
    private const uint storage_push_constant = 9;
    private const uint storage_storage_buffer = 12;

    private const uint execution_model_vertex = 0;
    private const uint execution_model_fragment = 4;

    public SpirvStage Stage { get; private init; }

    public string EntryPoint { get; private init; } = string.Empty;

    public IReadOnlyList<SpirvUniformBlock> UniformBlocks { get; private init; } = [];

    public IReadOnlyList<SpirvSampler> Samplers { get; private init; } = [];

    public IReadOnlyList<SpirvOtherResource> OtherResources { get; private init; } = [];

    /// <summary>
    /// The vertex shader's inputs, built-ins excluded. Empty for any other stage.
    /// </summary>
    public IReadOnlyList<SpirvVertexInput> VertexInputs { get; private init; } = [];

    public bool HasPushConstants { get; private init; }

    private SpirvReflection()
    {
    }

    /// <exception cref="FormatException">The bytes are not a well-formed SPIR-V module.</exception>
    public static SpirvReflection Parse(ReadOnlySpan<byte> spirv)
    {
        if (spirv.Length < 20 || spirv.Length % 4 != 0)
            throw new FormatException($"A SPIR-V module is a whole number of 32-bit words with a 5-word header; got {spirv.Length} bytes.");

        return new Parser(MemoryMarshal.Cast<byte, uint>(spirv)).Parse();
    }

    private sealed class TypeInfo
    {
        public ushort Opcode;

        /// <summary>
        /// Component/element/pointee/image type, depending on <see cref="Opcode"/>.
        /// </summary>
        public uint ElementType;

        /// <summary>
        /// Vector component count, matrix column count, bit width, or array-length constant id.
        /// </summary>
        public uint Count;

        public bool Signed;
        public uint StorageClass;
        public uint[] Members = [];
    }

    private sealed class Decorations
    {
        public bool Block;
        public bool BufferBlock;
        public bool BuiltIn;
        public uint? Set;
        public uint? Binding;
        public uint? Location;
        public uint? ArrayStride;
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<uint> words;

        private readonly Dictionary<uint, string> names = new Dictionary<uint, string>();
        private readonly Dictionary<(uint type, uint member), string> memberNames = new Dictionary<(uint, uint), string>();
        private readonly Dictionary<uint, Decorations> decorations = new Dictionary<uint, Decorations>();
        private readonly Dictionary<(uint type, uint member), uint> memberOffsets = new Dictionary<(uint, uint), uint>();
        private readonly Dictionary<(uint type, uint member), uint> memberMatrixStrides = new Dictionary<(uint, uint), uint>();
        private readonly Dictionary<(uint type, uint member), bool> memberBuiltIns = new Dictionary<(uint, uint), bool>();
        private readonly Dictionary<uint, TypeInfo> types = new Dictionary<uint, TypeInfo>();
        private readonly Dictionary<uint, uint> constants = new Dictionary<uint, uint>();
        private readonly HashSet<uint> specConstants = new HashSet<uint>();
        private readonly List<(uint id, uint type, uint storageClass)> variables = new List<(uint, uint, uint)>();

        private SpirvStage stage = SpirvStage.Other;
        private string entryPoint = string.Empty;

        public Parser(ReadOnlySpan<uint> words)
        {
            this.words = words;
        }

        public SpirvReflection Parse()
        {
            if (words[0] != magic_number)
                throw new FormatException($"Not a SPIR-V module (magic number 0x{words[0]:X8}).");

            int position = 5;

            while (position < words.Length)
            {
                uint first = words[position];
                int wordCount = (int)(first >> 16);
                ushort opcode = (ushort)(first & 0xFFFF);

                if (wordCount == 0 || position + wordCount > words.Length)
                    throw new FormatException($"Malformed SPIR-V instruction at word {position}.");

                decode(opcode, words.Slice(position + 1, wordCount - 1));
                position += wordCount;
            }

            return build();
        }

        private void decode(ushort opcode, ReadOnlySpan<uint> operands)
        {
            switch (opcode)
            {
                case op_name:
                    names[operands[0]] = readString(operands[1..]);
                    break;

                case op_member_name:
                    memberNames[(operands[0], operands[1])] = readString(operands[2..]);
                    break;

                case op_entry_point:
                    // Only the first entry point is described; the framework's shaders have one each.
                    if (entryPoint.Length > 0)
                        break;

                    stage = operands[0] switch
                    {
                        execution_model_vertex => SpirvStage.Vertex,
                        execution_model_fragment => SpirvStage.Fragment,
                        _ => SpirvStage.Other,
                    };
                    entryPoint = readString(operands[2..]);
                    break;

                case op_type_bool:
                case op_type_sampler:
                    types[operands[0]] = new TypeInfo { Opcode = opcode };
                    break;

                case op_type_int:
                    types[operands[0]] = new TypeInfo { Opcode = opcode, Count = operands[1], Signed = operands[2] != 0 };
                    break;

                case op_type_float:
                    types[operands[0]] = new TypeInfo { Opcode = opcode, Count = operands[1] };
                    break;

                case op_type_vector:
                case op_type_matrix:
                case op_type_array:
                    types[operands[0]] = new TypeInfo { Opcode = opcode, ElementType = operands[1], Count = operands[2] };
                    break;

                case op_type_image:
                case op_type_sampled_image:
                case op_type_runtime_array:
                    types[operands[0]] = new TypeInfo { Opcode = opcode, ElementType = operands[1] };
                    break;

                case op_type_struct:
                    types[operands[0]] = new TypeInfo { Opcode = opcode, Members = operands[1..].ToArray() };
                    break;

                case op_type_pointer:
                    types[operands[0]] = new TypeInfo { Opcode = opcode, StorageClass = operands[1], ElementType = operands[2] };
                    break;

                case op_constant:
                    // Operands: result type, result id, value. Only the low word matters for array lengths.
                    constants[operands[1]] = operands[2];
                    break;

                case op_spec_constant:
                    specConstants.Add(operands[1]);
                    break;

                case op_variable:
                    variables.Add((operands[1], operands[0], operands[2]));
                    break;

                case op_decorate:
                    decorate(operands[0], operands[1], operands.Length > 2 ? operands[2] : 0);
                    break;

                case op_member_decorate:
                {
                    var key = (operands[0], operands[1]);
                    uint value = operands.Length > 3 ? operands[3] : 0;

                    switch (operands[2])
                    {
                        case decoration_offset:
                            memberOffsets[key] = value;
                            break;

                        case decoration_matrix_stride:
                            memberMatrixStrides[key] = value;
                            break;

                        case decoration_built_in:
                            memberBuiltIns[key] = true;
                            break;
                    }

                    break;
                }
            }
        }

        private void decorate(uint target, uint decoration, uint value)
        {
            if (!decorations.TryGetValue(target, out var d))
                decorations[target] = d = new Decorations();

            switch (decoration)
            {
                case decoration_block:
                    d.Block = true;
                    break;

                case decoration_buffer_block:
                    d.BufferBlock = true;
                    break;

                case decoration_built_in:
                    d.BuiltIn = true;
                    break;

                case decoration_descriptor_set:
                    d.Set = value;
                    break;

                case decoration_binding:
                    d.Binding = value;
                    break;

                case decoration_location:
                    d.Location = value;
                    break;

                case decoration_array_stride:
                    d.ArrayStride = value;
                    break;
            }
        }

        private SpirvReflection build()
        {
            var blocks = new List<SpirvUniformBlock>();
            var samplers = new List<SpirvSampler>();
            var others = new List<SpirvOtherResource>();
            var inputs = new List<SpirvVertexInput>();
            bool hasPushConstants = false;

            foreach (var (id, pointerType, storageClass) in variables)
            {
                uint type = types.TryGetValue(pointerType, out var pointer) ? pointer.ElementType : 0;
                var d = decorations.GetValueOrDefault(id);
                uint set = d?.Set ?? 0;
                uint binding = d?.Binding ?? 0;

                switch (storageClass)
                {
                    case storage_uniform:
                    {
                        var typeDecorations = decorations.GetValueOrDefault(type);
                        string blockName = nameOf(type);

                        if (typeDecorations?.BufferBlock == true)
                            others.Add(new SpirvOtherResource(blockName, set, binding, SpirvOtherResourceKind.StorageBuffer));
                        else
                            blocks.Add(new SpirvUniformBlock(blockName, set, binding, sizeOf(type, 0), membersOf(type)));
                        break;
                    }

                    case storage_storage_buffer:
                        others.Add(new SpirvOtherResource(nameOf(type), set, binding, SpirvOtherResourceKind.StorageBuffer));
                        break;

                    case storage_uniform_constant:
                    {
                        uint arrayLength = 1;
                        uint element = type;

                        if (types.TryGetValue(type, out var arrayType) && arrayType.Opcode == op_type_array)
                        {
                            arrayLength = arrayLengthOf(arrayType);
                            element = arrayType.ElementType;
                        }

                        ushort elementOpcode = types.TryGetValue(element, out var elementType) ? elementType.Opcode : (ushort)0;

                        if (elementOpcode == op_type_sampled_image)
                            samplers.Add(new SpirvSampler(nameOf(id), set, binding, arrayLength));
                        else
                        {
                            var kind = elementOpcode switch
                            {
                                op_type_image => SpirvOtherResourceKind.Image,
                                op_type_sampler => SpirvOtherResourceKind.Sampler,
                                _ => SpirvOtherResourceKind.Unknown,
                            };

                            others.Add(new SpirvOtherResource(nameOf(id), set, binding, kind));
                        }

                        break;
                    }

                    case storage_push_constant:
                        hasPushConstants = true;
                        break;

                    case storage_input when stage == SpirvStage.Vertex:
                    {
                        if (d?.BuiltIn == true || isBuiltInBlock(type) || d?.Location is not uint location)
                            break;

                        describeVertexInput(type, out var kind, out uint count);
                        inputs.Add(new SpirvVertexInput(nameOf(id), location, kind, count));
                        break;
                    }
                }
            }

            blocks.Sort((a, b) => (a.Set, a.Binding).CompareTo((b.Set, b.Binding)));
            samplers.Sort((a, b) => (a.Set, a.Binding).CompareTo((b.Set, b.Binding)));
            inputs.Sort((a, b) => a.Location.CompareTo(b.Location));

            return new SpirvReflection
            {
                Stage = stage,
                EntryPoint = entryPoint,
                UniformBlocks = blocks,
                Samplers = samplers,
                OtherResources = others,
                VertexInputs = inputs,
                HasPushConstants = hasPushConstants,
            };
        }

        private string nameOf(uint id) => names.GetValueOrDefault(id, string.Empty);

        private uint arrayLengthOf(TypeInfo arrayType)
        {
            if (constants.TryGetValue(arrayType.Count, out uint length))
                return length;

            if (specConstants.Contains(arrayType.Count))
                throw new FormatException("Array lengths set by specialization constants are not supported.");

            throw new FormatException($"Array length refers to unknown constant %{arrayType.Count}.");
        }

        private bool isBuiltInBlock(uint type)
        {
            if (!types.TryGetValue(type, out var info) || info.Opcode != op_type_struct)
                return false;

            for (uint i = 0; i < info.Members.Length; i++)
            {
                if (memberBuiltIns.ContainsKey((type, i)))
                    return true;
            }

            return false;
        }

        private void describeVertexInput(uint type, out SpirvScalarKind kind, out uint count)
        {
            count = 1;

            if (types.TryGetValue(type, out var info) && info.Opcode == op_type_vector)
            {
                count = info.Count;
                type = info.ElementType;
            }

            kind = scalarKindOf(type);
        }

        private SpirvScalarKind scalarKindOf(uint type)
        {
            if (!types.TryGetValue(type, out var info))
                return SpirvScalarKind.Other;

            return info.Opcode switch
            {
                op_type_float => SpirvScalarKind.Float,
                op_type_int => info.Signed ? SpirvScalarKind.SignedInt : SpirvScalarKind.UnsignedInt,
                op_type_bool => SpirvScalarKind.Bool,
                _ => SpirvScalarKind.Other,
            };
        }

        private IReadOnlyList<SpirvBlockMember> membersOf(uint structType)
        {
            if (!types.TryGetValue(structType, out var info) || info.Opcode != op_type_struct)
                return [];

            var members = new SpirvBlockMember[info.Members.Length];

            for (uint i = 0; i < info.Members.Length; i++)
            {
                members[i] = new SpirvBlockMember(
                    memberNames.GetValueOrDefault((structType, i), string.Empty),
                    memberOffsets.GetValueOrDefault((structType, i)),
                    sizeOf(info.Members[i], memberMatrixStrides.GetValueOrDefault((structType, i))));
            }

            return members;
        }

        /// <summary>
        /// The byte size a value of <paramref name="type"/> occupies inside a block, using the explicit
        /// offsets and strides the compiler laid the block out with. A struct's size runs to the end of
        /// its furthest member, which is what a descriptor's range has to cover.
        /// </summary>
        private uint sizeOf(uint type, uint matrixStride)
        {
            if (!types.TryGetValue(type, out var info))
                throw new FormatException($"Block member refers to unknown type %{type}.");

            switch (info.Opcode)
            {
                case op_type_bool:
                    return 4;

                case op_type_int:
                case op_type_float:
                    return info.Count / 8;

                case op_type_vector:
                    return info.Count * sizeOf(info.ElementType, 0);

                case op_type_matrix:
                    return info.Count * (matrixStride != 0 ? matrixStride : sizeOf(info.ElementType, 0));

                case op_type_array:
                {
                    uint stride = decorations.GetValueOrDefault(type)?.ArrayStride ?? sizeOf(info.ElementType, matrixStride);
                    return arrayLengthOf(info) * stride;
                }

                case op_type_struct:
                {
                    uint end = 0;

                    for (uint i = 0; i < info.Members.Length; i++)
                    {
                        uint offset = memberOffsets.GetValueOrDefault((type, i));
                        uint size = sizeOf(info.Members[i], memberMatrixStrides.GetValueOrDefault((type, i)));
                        end = Math.Max(end, offset + size);
                    }

                    return end;
                }

                case op_type_runtime_array:
                    throw new FormatException("Runtime-sized arrays are only valid in storage buffers.");

                default:
                    throw new FormatException($"Opcode {info.Opcode} cannot appear inside a uniform block.");
            }
        }

        private static string readString(ReadOnlySpan<uint> operands)
        {
            var bytes = MemoryMarshal.AsBytes(operands);
            int terminator = bytes.IndexOf((byte)0);
            return Encoding.UTF8.GetString(terminator >= 0 ? bytes[..terminator] : bytes);
        }
    }
}
