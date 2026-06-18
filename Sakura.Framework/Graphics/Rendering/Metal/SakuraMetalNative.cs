// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Graphics.Rendering.Metal;

/// <summary>
/// One vertex attribute for the Metal pipeline's vertex descriptor. Layout must match the native
/// <c>SakuraMetalVertexAttribute</c> struct (three ints). Public so it can appear in
/// <see cref="MetalShader"/>'s public constructor signature.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MetalVertexAttribute
{
    public int AttributeIndex;
    public int ComponentCount;
    public int Offset;
}

/// <summary>
/// Diagnostic info about the Metal device, filled by <c>sakura_metal_get_info</c>. Layout must match
/// the native <c>SakuraMetalInfo</c> struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MetalDeviceInfo
{
    public int MaxThreadsPerThreadgroup;
    public int HasUnifiedMemory;
    public int SupportsFamilyApple;
    public int SupportsFamilyMac;
    public ulong RecommendedMaxWorkingSetSize;
}

/// <summary>
/// P/Invoke bindings for <c>libsakura-metal</c>, the thin C bridge over Apple's Metal framework
/// (see <c>native/sakura-metal/SakuraMetal.h</c>).
/// </summary>
internal static class SakuraMetalNative
{
    private const string lib_name = "libsakura-metal";

    /// <summary>
    /// Installs the iOS DLL import resolver (the framework is embedded as
    /// <c>sakura-metal.framework</c> under <c>@rpath</c>). No-op on other platforms, where the
    /// dylib/so is found by name. Call once before any other entry point.
    /// </summary>
    public static void SetupLibraryResolvers()
    {
        if (OperatingSystem.IsIOS())
        {
            NativeLibrary.SetDllImportResolver(
                typeof(SakuraMetalNative).Assembly,
                (_, assembly, path) =>
                    NativeLibrary.Load("@rpath/sakura-metal.framework/sakura-metal", assembly, path));
        }
    }

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sakura_metal_create(nint caMetalLayer);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_destroy(nint device);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_metal_get_info(nint device, MetalDeviceInfo* info, byte* nameBuffer, int nameCapacity);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_begin_frame(nint device, float r, float g, float b, float a);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_end_frame(nint device);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_resize(nint device, int width, int height, float contentsScale);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe nint sakura_metal_create_pipeline(
        nint device,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string vertexMsl,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fragmentMsl,
        MetalVertexAttribute* attributes,
        int attributeCount,
        int vertexStride,
        int blendMode);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_destroy_pipeline(nint pipeline);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_set_pipeline(nint device, nint pipeline);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_metal_set_vertex_uniform(nint device, void* data, int length, int index);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_metal_set_fragment_uniform(nint device, void* data, int length, int index);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_metal_draw_triangles(nint device, void* data, int vertexCount, int vertexStride);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sakura_metal_create_render_target(nint device, int width, int height);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_begin_offscreen(nint device, nint texture, float r, float g, float b, float a);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_end_offscreen(nint device);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sakura_metal_create_texture(nint device, int width, int height);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_metal_upload_texture(nint texture, void* data, int width, int height);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_metal_upload_texture_region(nint texture, int x, int y, int width, int height, void* data);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_destroy_texture(nint texture);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sakura_metal_create_plane_texture(nint device, int width, int height);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_metal_upload_plane(nint texture, void* data, int width, int height, int bytesPerRow);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_set_fragment_texture(nint device, nint texture, int slot);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_metal_set_fragment_texture_wrap(nint device, nint texture, int slot, int repeat);
}
