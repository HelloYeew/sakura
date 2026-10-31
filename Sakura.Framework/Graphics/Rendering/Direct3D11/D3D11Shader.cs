// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// A Direct3D 11 shader program
/// </summary>
public sealed class D3D11Shader : IShader
{
    [Flags]
    public enum Stage
    {
        Vertex = 1,
        Fragment = 2,
    }

    /// <summary>
    /// Where a named uniform block binds, which stages and which cbuffer register.
    /// </summary>
    public readonly record struct UniformBinding(Stage Stage, int Slot);

    private const ShaderFlags compile_flags = ShaderFlags.OptimizationLevel3;

    /// <summary>
    /// Bump when what a bytecode cache entry holds changes, so old entries are never read back.
    /// </summary>
    private const int bytecode_cache_version = 1;

    /// <summary>
    /// Bytecode compiled or loaded in this process, by cache key. Shaders are created per instance
    /// (every BufferedContainer draw node makes its own blur and grayscale shaders, every video sprite
    /// its own video shaders), and without this each of them would run FXC on the draw thread.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> compiled_bytecode = new ConcurrentDictionary<string, byte[]>();

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    private ID3D11VertexShader vertexShader;
    private ID3D11PixelShader pixelShader;
    private ID3D11InputLayout inputLayout;

    private readonly Dictionary<string, UniformBinding> uniformBlockBindings;
    private readonly Dictionary<(Stage, int), ID3D11Buffer> constantBuffers = new();

    public nint Handle => vertexShader?.NativePointer ?? nint.Zero;

    // The paths name the GLSL sources each HLSL string was cross-compiled from, for compiler messages.
    // The cache keeps compiled bytecode across launches; without one, only this process's compiles are
    // reused.
    public D3D11Shader(ID3D11Device device, ID3D11DeviceContext context,
        string vertexPath, string vertexHlsl, string fragmentPath, string fragmentHlsl, InputElementDescription[] inputElements,
        IReadOnlyDictionary<string, UniformBinding> uniformBlockBindings = null, DiskCache cache = null)
    {
        this.device = device;
        this.context = context;
        this.uniformBlockBindings = uniformBlockBindings != null
            ? new Dictionary<string, UniformBinding>(uniformBlockBindings)
            : new Dictionary<string, UniformBinding>();

        byte[] vsBytecode = getOrCompile(vertexHlsl, $"{vertexPath}.hlsl", "vs_5_0", cache);
        byte[] psBytecode = getOrCompile(fragmentHlsl, $"{fragmentPath}.hlsl", "ps_5_0", cache);

        vertexShader = device.CreateVertexShader(vsBytecode);
        pixelShader = device.CreatePixelShader(psBytecode);
        inputLayout = device.CreateInputLayout(inputElements, vsBytecode);
    }

    private static byte[] getOrCompile(string hlsl, string sourceName, string profile, DiskCache cache)
    {
        string key = $"fxc#{DiskCache.HashString(hlsl)}#{profile}#{(int)compile_flags}#{bytecode_cache_version}";

        if (compiled_bytecode.TryGetValue(key, out byte[] bytecode))
            return bytecode;

        if (cache != null && cache.TryRead(key, out bytecode))
        {
            Logger.Verbose($"Direct3D11 shader {sourceName} ({profile}) loaded from the shader cache. Clear the cache to see its compile time and sampling again.");
        }
        else
        {
            bytecode = compile(hlsl, sourceName, profile);
            cache?.Write(key, bytecode);
        }

        compiled_bytecode[key] = bytecode;
        return bytecode;
    }

    private static byte[] compile(string hlsl, string sourceName, string profile)
    {
        long compileStart = Stopwatch.GetTimestamp();

        // SPIRV-Cross emits an HLSL entry point named "main" (unlike MSL's "main0").
        Result result = Compiler.Compile(hlsl, null, null, "main", sourceName, profile, compile_flags, out Blob code, out Blob error);

        double compileMilliseconds = Stopwatch.GetElapsedTime(compileStart).TotalMilliseconds;

        if (result.Failure || code == null)
        {
            string message = error != null ? error.AsString() : $"HRESULT 0x{result.Code:X8}";
            error?.Dispose();
            throw new InvalidOperationException($"Failed to compile HLSL ({profile}): {message}");
        }

        // A successful compile can still carry warnings, and some of them are about performance: X4121
        // means a texture sample inside a branch had to be moved out of it.
        if (error != null)
            Logger.Verbose($"FXC warnings for {sourceName} ({profile}):\n{error.AsString().TrimEnd()}");

        byte[] bytes = code.AsBytes();
        code.Dispose();
        error?.Dispose();

        // Compiles run on the draw thread, so the time is a hitch in that frame.
        string summary = $"Direct3D11 shader {sourceName} ({profile}) compiled in {compileMilliseconds:N1} ms";

        if (profile.StartsWith("ps", StringComparison.Ordinal))
            summary += describeSampling(bytes);

        Logger.Verbose(summary);
        return bytes;
    }

    /// <summary>
    /// Counts the texture samples in a compiled pixel shader, and how many of them stayed inside a
    /// branch. Where each pixel picks its texture by branching, as the main shader does, a sample still
    /// inside its branch only runs for the pixels that take it; one the compiler moved out runs for
    /// every pixel.
    /// </summary>
    private static unsafe string describeSampling(byte[] bytecode)
    {
        string disassembly;

        try
        {
            fixed (byte* data = bytecode)
            {
                using Blob blob = Compiler.Disassemble((nint)data, (nuint)bytecode.Length, DisasmFlags.None, null);
                disassembly = blob.AsString();
            }
        }
        catch (Exception e)
        {
            return $"; disassembly unavailable ({e.Message})";
        }

        int samples = 0;
        int samplesInBranch = 0;
        int samplesInLoop = 0;
        int branchDepth = 0;
        int loopDepth = 0;
        string? instructionSlots = null;

        foreach (string rawLine in disassembly.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                // "// Approximately 120 instruction slots used"
                if (line.Contains("instruction slots used", StringComparison.Ordinal))
                    instructionSlots = line.TrimStart('/', ' ');

                continue;
            }

            int end = line.IndexOfAny([' ', '(']);
            string opcode = end < 0 ? line : line[..end];

            switch (opcode)
            {
                case "if_z":
                case "if_nz":
                case "switch":
                    branchDepth++;
                    break;

                case "endif":
                case "endswitch":
                    branchDepth--;
                    break;

                case "loop":
                    loopDepth++;
                    break;

                case "endloop":
                    loopDepth--;
                    break;

                default:
                    // sample, sample_l, sample_d, sample_indexable, ...
                    if (opcode.StartsWith("sample", StringComparison.Ordinal))
                    {
                        samples++;

                        if (branchDepth > 0)
                            samplesInBranch++;

                        if (loopDepth > 0)
                            samplesInLoop++;
                    }

                    break;
            }
        }

        return $"; {samples} texture samples, {samplesInBranch} inside a branch, {samplesInLoop} inside a loop"
               + (instructionSlots != null ? $"; {instructionSlots}" : string.Empty);
    }

    /// <summary>
    /// Binds the vertex + pixel shaders and input layout on the immediate context.
    /// </summary>
    public void Use()
    {
        context.VSSetShader(vertexShader);
        context.PSSetShader(pixelShader);
        context.IASetInputLayout(inputLayout);
    }

    // Per-name scalar uniforms aren't used by the D3D11 path (everything travels as constant-buffer
    // blocks). The video shader's `u_Texture` sampler index is implicit (texture bound to slot 0).
    public void SetUniform(string name, int value) { }
    public void SetUniform(string name, float value) { }
    public void SetUniform(string name, bool value) { }
    public void SetUniform(string name, Matrix4x4 value) { }
    public void SetUniform(string name, Vector2 value) { }
    public void SetUniform(string name, Vector4 value) { }
    public void SetUniform(string name, Color value) { }
    public void SetUniformIntArray(string name, int[] values) { }
    public void SetUniform(string name, float[] mat3X3) { }

    /// <summary>
    /// Uploads a std140-laid-out uniform block by name to its (stages, cbuffer register) and binds it.
    /// </summary>
    public unsafe void SetUniformBlock<T>(string blockName, in T data) where T : unmanaged
    {
        if (!uniformBlockBindings.TryGetValue(blockName, out var binding))
            return;

        // D3D constant buffers must be a multiple of 16 bytes.
        int size = (sizeof(T) + 15) & ~15;
        var cb = getOrCreateConstantBuffer(binding, size);

        MappedSubresource mapped = context.Map(cb, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        fixed (T* src = &data)
            Buffer.MemoryCopy(src, (void*)mapped.DataPointer, size, sizeof(T));
        context.Unmap(cb, 0);

        if ((binding.Stage & Stage.Vertex) != 0)
            context.VSSetConstantBuffer((uint)binding.Slot, cb);

        if ((binding.Stage & Stage.Fragment) != 0)
            context.PSSetConstantBuffer((uint)binding.Slot, cb);
    }

    private ID3D11Buffer getOrCreateConstantBuffer(UniformBinding binding, int size)
    {
        var key = (binding.Stage, binding.Slot);
        if (constantBuffers.TryGetValue(key, out var existing))
            return existing;

        var cb = device.CreateBuffer(new BufferDescription(
            (uint)size, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        constantBuffers[key] = cb;
        return cb;
    }

    public void Dispose()
    {
        foreach (var cb in constantBuffers.Values)
            cb?.Dispose();
        constantBuffers.Clear();

        inputLayout?.Dispose();
        inputLayout = null;
        pixelShader?.Dispose();
        pixelShader = null;
        vertexShader?.Dispose();
        vertexShader = null;
    }
}
