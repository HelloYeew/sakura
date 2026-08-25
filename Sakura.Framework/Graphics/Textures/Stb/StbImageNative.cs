// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Graphics.Textures.Stb;

/// <summary>
/// P/Invoke bindings for <c>libsakura-image</c>, the stb_image + stb_image_resize2 shim behind
/// <see cref="StbImageLoader"/> (see <c>native/sakura-image/sakura_image.h</c>).
/// </summary>
internal static class StbImageNative
{
    // Unprefixed on purpose: the runtime adds the "lib" prefix and the platform suffix on Unix, so this
    // one name resolves libsakura-image.dylib, libsakura-image.so and sakura-image.dll.
    private const string lib_name = "sakura-image";

    /// <summary>
    /// The ABI this assembly was built against. Must match <c>SAKURA_IMAGE_ABI_VERSION</c> in
    /// <c>sakura_image.h</c>; a shipped library that disagrees is refused rather than trusted.
    /// </summary>
    public const int ABI_VERSION = 2;

    public const int OK = 0;
    public const int ERROR = -1;
    public const int INVALID = -2;
    public const int NOMEM = -3;

    private static bool? available;

    /// <summary>
    /// Whether the native library is present and reports a matching ABI. Latched after the first call,
    /// so a machine without the library pays one failed resolve for the process rather than one per
    /// image.
    /// </summary>
    /// <remarks>
    /// The latch is what makes the RID gap survivable: a platform the native was never built for simply
    /// answers false forever and the caller uses ImageSharp.
    /// </remarks>
    public static bool IsAvailable
    {
        get
        {
            if (available.HasValue)
                return available.Value;

            try
            {
                int version = sakura_image_abi_version();

                if (version != ABI_VERSION)
                {
                    Logger.Warning($"libsakura-image reports ABI {version}, but this build expects {ABI_VERSION}. " +
                                   "Image decoding will use ImageSharp.");
                    available = false;
                }
                else
                {
                    available = true;
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                Logger.Verbose($"libsakura-image is not available ({e.GetType().Name}); image decoding will use ImageSharp.");
                available = false;
            }

            return available.Value;
        }
    }

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_image_abi_version();

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sakura_image_stb_version();

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sakura_image_stb_resize_version();

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sakura_image_formats();

    /// <summary>
    /// The vendored stb_image version the loaded library reports, e.g. 2.30.
    /// </summary>
    public static string StbVersion => read(sakura_image_stb_version);

    /// <summary>
    /// The vendored stb_image_resize2 version, e.g. <c>2.18</c>.
    /// </summary>
    public static string StbResizeVersion => read(sakura_image_stb_resize_version);

    /// <summary>
    /// The formats this build of the native can decode, e.g. JPEG, PNG, BMP, GIF. Derived in the
    /// shim from the defines the decoders are compiled behind, so it reports the build rather than
    /// repeating a claim about it.
    /// </summary>
    public static string Formats => read(sakura_image_formats);

    private static string read(Func<IntPtr> entryPoint)
    {
        try
        {
            return Marshal.PtrToStringUTF8(entryPoint()) ?? "unknown";
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return "unavailable";
        }
    }

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int sakura_image_info(byte* encoded, int length, out int width, out int height);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int sakura_image_load(byte* encoded, int length,
                                                      int srcX, int srcY, int srcWidth, int srcHeight,
                                                      byte* dst, int dstWidth, int dstHeight, int dstLength);

    /// <summary>
    /// A human-readable description of one of the codes above, for exception messages.
    /// </summary>
    public static string Describe(int code) => code switch
    {
        OK => "ok",
        ERROR => "the decoder rejected the data (unsupported format, or corrupt)",
        INVALID => "invalid arguments",
        NOMEM => "an allocation failed",
        _ => $"unknown code {code}"
    };
}
