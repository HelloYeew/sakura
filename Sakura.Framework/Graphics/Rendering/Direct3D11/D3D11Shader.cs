// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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
    private readonly ID3D11DeviceContext context;

    private ID3D11VertexShader vertexShader;
    private ID3D11PixelShader pixelShader;
    private ID3D11InputLayout inputLayout;

    public nint Handle => vertexShader?.NativePointer ?? nint.Zero;

    public D3D11Shader(ID3D11Device device, ID3D11DeviceContext context,
        string vertexHlsl, string fragmentHlsl, InputElementDescription[] inputElements)
    {
        this.context = context;

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
    // blocks)
    public void SetUniform(string name, int value) { }
    public void SetUniform(string name, float value) { }
    public void SetUniform(string name, bool value) { }
    public void SetUniform(string name, Matrix4x4 value) { }
    public void SetUniform(string name, Vector2 value) { }
    public void SetUniform(string name, Vector4 value) { }
    public void SetUniform(string name, Color value) { }
    public void SetUniformIntArray(string name, int[] values) { }
    public void SetUniform(string name, float[] mat3X3) { }

    public void SetUniformBlock<T>(string blockName, in T data) where T : unmanaged { }

    public void Dispose()
    {
        inputLayout?.Dispose();
        inputLayout = null;
        pixelShader?.Dispose();
        pixelShader = null;
        vertexShader?.Dispose();
        vertexShader = null;
    }
}
