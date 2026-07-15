// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
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
    public enum Stage
    {
        Vertex,
        Fragment,
    }

    /// <summary>
    /// Where a named uniform block binds, which stage and which cbuffer register.
    /// </summary>
    public readonly record struct UniformBinding(Stage Stage, int Slot);

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    private ID3D11VertexShader vertexShader;
    private ID3D11PixelShader pixelShader;
    private ID3D11InputLayout inputLayout;

    private readonly Dictionary<string, UniformBinding> uniformBlockBindings;
    private readonly Dictionary<(Stage, int), ID3D11Buffer> constantBuffers = new();

    public nint Handle => vertexShader?.NativePointer ?? nint.Zero;

    public D3D11Shader(ID3D11Device device, ID3D11DeviceContext context,
        string vertexHlsl, string fragmentHlsl, InputElementDescription[] inputElements,
        IReadOnlyDictionary<string, UniformBinding> uniformBlockBindings = null)
    {
        this.device = device;
        this.context = context;
        this.uniformBlockBindings = uniformBlockBindings != null
            ? new Dictionary<string, UniformBinding>(uniformBlockBindings)
            : new Dictionary<string, UniformBinding>();

        byte[] vsBytecode = compile(vertexHlsl, "vs_5_0");
        byte[] psBytecode = compile(fragmentHlsl, "ps_5_0");

        vertexShader = device.CreateVertexShader(vsBytecode);
        pixelShader = device.CreatePixelShader(psBytecode);
        inputLayout = device.CreateInputLayout(inputElements, vsBytecode);
    }

    private static byte[] compile(string hlsl, string profile)
    {
        // SPIRV-Cross emits an HLSL entry point named "main" (unlike MSL's "main0").
        Result result = Compiler.Compile(hlsl, "main", "sakura", profile, out Blob code, out Blob error);

        if (result.Failure || code == null)
        {
            string message = error != null ? error.AsString() : $"HRESULT 0x{result.Code:X8}";
            error?.Dispose();
            throw new InvalidOperationException($"Failed to compile HLSL ({profile}): {message}");
        }

        byte[] bytes = code.AsBytes();
        code.Dispose();
        error?.Dispose();
        return bytes;
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
    /// Uploads a std140-laid-out uniform block by name to its (stage, cbuffer register) and binds it.
    /// Used by the effect passes (ProjectionBlock, BlurBlock, GrayscaleBlock) and video (VideoBlock).
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

        if (binding.Stage == Stage.Vertex)
            context.VSSetConstantBuffer((uint)binding.Slot, cb);
        else
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
