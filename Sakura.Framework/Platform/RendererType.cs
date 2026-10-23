// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Platform;

public enum RendererType
{
    Automatic,
    Metal,
    Direct3D11,
    Vulkan,

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    OpenGL
}

/// <summary>
/// Helpers for determining which <see cref="RendererType"/> values make sense on the current platform.
/// </summary>
public static class RendererTypes
{
    /// <summary>
    /// The renderer types <see cref="RendererType.Automatic"/> tries on the current platform, most preferred
    /// first, and so the order the host falls back through when one fails to start.
    /// <see cref="RendererType.OpenGL"/> is the last resort on desktops. Android is Vulkan only and iOS Metal only:
    /// neither has the desktop OpenGL the GL renderer needs.
    /// </summary>
    /// <remarks>
    /// Direct3D 11 stays first on Windows, with Vulkan after it. Linux puts Vulkan first, as the only
    /// renderer there that decodes video in hardware. On macOS Vulkan isn't offered at all: Metal is the
    /// backend there, and Vulkan through KosmicKrisp is for developing the Vulkan renderer, reached only by
    /// naming it in <c>framework.ini</c>.
    /// </remarks>
    public static IReadOnlyList<RendererType> GetPlatformRenderers()
    {
        if (RuntimeInfo.OS == RuntimeInfo.Platform.Android)
            return [RendererType.Vulkan];

        if (RuntimeInfo.OS == RuntimeInfo.Platform.iOS)
            return [RendererType.Metal];

        if (RuntimeInfo.IsMacOS)
            return [RendererType.Metal, RendererType.OpenGL];

        if (RuntimeInfo.IsWindows)
            return [RendererType.Direct3D11, RendererType.Vulkan, RendererType.OpenGL];

        return [RendererType.Vulkan, RendererType.OpenGL];
    }

    /// <summary>
    /// The renderer types a user can select on the current platform, in display order:
    /// <see cref="RendererType.Automatic"/> first, then each one <see cref="GetPlatformRenderers"/> tries.
    /// For a settings dropdown: on macOS this is <c>[Automatic, Metal, OpenGL]</c>, on Linux
    /// <c>[Automatic, Vulkan, OpenGL]</c>.
    /// </summary>
    public static IReadOnlyList<RendererType> GetSuitableRenderers()
    {
        var result = new List<RendererType> { RendererType.Automatic };
        result.AddRange(GetPlatformRenderers());
        return result;
    }
}
